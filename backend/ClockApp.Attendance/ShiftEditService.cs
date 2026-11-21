using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

/// <summary>An employee's day-level edit: set the clock-in and/or clock-out of a Zurich date (times already UTC).</summary>
public sealed record ShiftEditRequest(
    int UserId, DateOnly Date, DateTimeOffset? ClockInUtc, DateTimeOffset? ClockOutUtc, string ReasonCode, string? ReasonText);

/// <summary>What an edit would do: the effective records before and after, and the audit rows to append.</summary>
public sealed record ShiftPlan(
    IReadOnlyList<EffectiveRecord> Before, IReadOnlyList<EffectiveRecord> After,
    IReadOnlyList<AttendanceCorrection> Rows, long? RecordToClose);

/// <summary>
/// Employee-owned corrections. A day with no shift gets a manual interval (both times required); a day with a shift
/// gets its FIRST clock-in and/or LAST clock-out adjusted. Punches are never altered: every change is an appended,
/// reasoned correction row (shared BatchId). Planning is pure (also the preview); committing runs under the per-user
/// lock in one transaction. "Now" is a parameter: this project never reads a clock.
/// </summary>
public sealed class ShiftEditService(AttendanceDbContext db, IMonthStateReader months)
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [ReasonCodes.ForgotClockIn] = "Forgot to clock in",
        [ReasonCodes.ForgotClockOut] = "Forgot to clock out",
        [ReasonCodes.WrongTimePunched] = "Wrong time punched",
        [ReasonCodes.DuplicateOrAccidental] = "Duplicate or accidental punch",
        [ReasonCodes.WorkedOffsite] = "Worked offsite or other location",
        [ReasonCodes.SystemProblem] = "System or time-service problem",
        [ReasonCodes.Other] = "Other",
    };

    public async Task<ShiftPlan> PreviewAsync(ShiftEditRequest req, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var effective = await EffectiveAttendance.LoadAsync(db, req.UserId, ct: ct);
        return await PlanAsync(effective, req, nowUtc, Guid.NewGuid(), ct);
    }

    public async Task<ShiftPlan> CommitAsync(ShiftEditRequest req, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserLock.AcquireAsync(db, req.UserId, ct);

        var effective = await EffectiveAttendance.LoadAsync(db, req.UserId, ct: ct);
        var plan = await PlanAsync(effective, req, nowUtc, Guid.NewGuid(), ct);

        db.AttendanceCorrections.AddRange(plan.Rows);
        await db.SaveChangesAsync(ct);
        // Closing a forgotten clock-out frees the employee to clock in again.
        if (plan.RecordToClose is { } id)
            await db.AttendanceRecords.Where(r => r.Id == id && r.IsOpen).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsOpen, false), ct);
        await tx.CommitAsync(ct);
        return plan;
    }

    private async Task<ShiftPlan> PlanAsync(
        List<EffectiveRecord> effective, ShiftEditRequest req, DateTimeOffset nowUtc, Guid batch, CancellationToken ct)
    {
        if (!ReasonCodes.All.Contains(req.ReasonCode)) throw Invalid("Choose one of the listed reasons.");
        var text = req.ReasonText?.Trim() ?? "";
        if (req.ReasonCode == ReasonCodes.Other && (text.Length < CorrectionService.MinReasonLength))
            throw Invalid($"Describe the reason ({CorrectionService.MinReasonLength}-300 characters).");
        if (text.Length > 300) throw Invalid("The reason text is too long (300 characters at most).");
        var reason = req.ReasonCode == ReasonCodes.Other ? text : text.Length > 0 ? $"{Labels[req.ReasonCode]}: {text}" : Labels[req.ReasonCode];

        if (req.ClockInUtc is null && req.ClockOutUtc is null) throw Invalid("Enter a clock-in and/or a clock-out time.");
        if (await months.GetStateAsync(req.UserId, req.Date.Year, req.Date.Month, ct) != MonthState.Open)
            throw Invalid("This month is locked and can no longer be changed.");

        var start = await db.Users.AsNoTracking().Where(u => u.Id == req.UserId).Select(u => u.EmploymentStartDate).SingleAsync(ct);
        if (start is { } s0 && req.Date < s0)
            throw Invalid($"You can only change days from your employment start date ({s0:dd.MM.yyyy}) on.");

        var day = effective.Where(e => e.WorkDate == req.Date).OrderBy(e => e.In).ToList();
        var rows = new List<AttendanceCorrection>();
        var after = effective.ToList();
        long? close = null;

        AttendanceCorrection Row(CorrectionKind kind, EffectiveRecord? target, DateTimeOffset newIn, DateTimeOffset? newOut) => new()
        {
            Kind = kind, AttendanceRecordId = target?.RecordId, TargetCorrectionId = target?.AddCorrectionId,
            BatchId = batch, ReasonCode = req.ReasonCode, UserId = req.UserId, WorkDate = ZurichTime.LocalDateOf(newIn),
            CorrectedClockInUtc = newIn, CorrectedClockOutUtc = newOut,
            PreviousClockInUtc = target?.In, PreviousClockOutUtc = target?.Out,
            Reason = reason, CreatedByUserId = req.UserId, CreatedAtUtc = nowUtc,
        };

        if (day.Count == 0)
        {
            if (req.ClockInUtc is null || req.ClockOutUtc is null) throw Invalid("This day has no shift yet: enter both clock-in and clock-out.");
            Validate(req.ClockInUtc.Value, req.ClockOutUtc, nowUtc, req.Date, effective, [], true, true, null);
            rows.Add(Row(CorrectionKind.Add, null, req.ClockInUtc.Value, req.ClockOutUtc));
            after.Add(new EffectiveRecord(null, 0, req.ClockInUtc.Value, req.ClockOutUtc, req.Date, true, false, false, null, null));
        }
        else
        {
            var first = day[0];
            var last = day[^1];
            var changes = new Dictionary<EffectiveRecord, (DateTimeOffset In, DateTimeOffset? Out)>();
            if (req.ClockInUtc is { } ci) changes[first] = (ci, first.Out);
            if (req.ClockOutUtc is { } co)
                changes[last] = (changes.TryGetValue(last, out var c) ? c.In : last.In, co);

            var touched = changes.Where(kv => kv.Value.In != kv.Key.In || kv.Value.Out != kv.Key.Out).ToList();
            if (touched.Count == 0) throw Invalid("The edit does not change anything.");

            foreach (var (target, (newIn, newOut)) in touched)
            {
                Validate(newIn, newOut, nowUtc, req.Date, effective, touched.Select(t => t.Key).ToList(), newIn != target.In, newOut != target.Out, target.Out);
                rows.Add(Row(CorrectionKind.Adjust, target, newIn, newOut));
                var closes = target.IsOpen && newOut is not null;
                if (closes) close = target.RecordId;
                after[after.IndexOf(target)] = target with { In = newIn, Out = newOut, IsAdjusted = true, IsOpen = target.IsOpen && newOut is null };
            }
        }
        return new ShiftPlan(effective, after, rows, close);
    }

    private static void Validate(
        DateTimeOffset newIn, DateTimeOffset? newOut, DateTimeOffset nowUtc, DateOnly date,
        List<EffectiveRecord> effective, List<EffectiveRecord> excluded, bool inChanged, bool outChanged, DateTimeOffset? currentOut)
    {
        if (ZurichTime.LocalDateOf(newIn) != date) throw Invalid("The clock-in must be on the day you are editing.");
        if (newIn > nowUtc) throw Invalid(Future("Clock-in", newIn, nowUtc));
        if (newOut is { } o)
        {
            if (o <= newIn)
            {
                string t(DateTimeOffset x) => ZurichTime.ToLocal(x).ToString("HH:mm");
                throw Invalid(outChanged && !inChanged ? $"The new clock-out {t(o)} is earlier than your clock-in {t(newIn)}; enter a time after {t(newIn)}." + (currentOut is { } c && t(c) != t(newIn) ? $" (Your current clock-out is {t(c)}.)" : "")
                    : inChanged && !outChanged ? $"The new clock-in {t(newIn)} is after your current clock-out {t(o)}. Change the clock-out too."
                    : $"The clock-out {t(o)} must be after the clock-in {t(newIn)}.");
            }
            if (o - newIn > CorrectionService.MaxShiftLength)
                throw Invalid($"A shift cannot be longer than 24 hours (this one is {(o - newIn).TotalHours:0.#} h).");
            if (o > nowUtc) throw Invalid(Future("Clock-out", o, nowUtc));
        }
        var clash = effective.Where(e => !excluded.Contains(e)).FirstOrDefault(e => EffectiveAttendance.Overlaps(newIn, newOut, e.In, e.Out));
        if (clash is not null) throw Invalid("These times overlap another shift of yours.");
    }

    private static string Future(string what, DateTimeOffset t, DateTimeOffset now) =>
        $"{what} {ZurichTime.ToLocal(t):yyyy-MM-dd HH:mm} is in the future (it is now {ZurichTime.ToLocal(now):yyyy-MM-dd HH:mm} in Zurich).";

    private static CorrectionException Invalid(string m) => new(CorrectionError.Invalid, m);
}
