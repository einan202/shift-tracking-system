using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

public enum CorrectionError { NotFound, Invalid }

public sealed class CorrectionException(CorrectionError error, string message) : Exception(message)
{
    public CorrectionError Error { get; } = error;
}

/// <summary>Times are already UTC (the API layer converts the admin's Zurich local input).</summary>
public sealed record CorrectionRequest(
    CorrectionKind Kind, int UserId, long? AttendanceRecordId,
    DateTimeOffset? ClockInUtc, DateTimeOffset? ClockOutUtc, string? Reason);

/// <summary>
/// Admin corrections. Original punches are NEVER edited: every change is an appended AttendanceCorrections
/// row (reason + previous values = audit trail). The only update to AttendanceRecords is flipping IsOpen when
/// a correction closes a forgotten clock-out, so the employee can clock in again.
///
/// CLOCK NOTE: corrections are manual admin input, not punches. The "now" used for the not-in-the-future
/// check and for CreatedAtUtc is a PARAMETER supplied by the API layer (server clock); this project never
/// reads a clock itself.
/// </summary>
public sealed class CorrectionService(AttendanceDbContext db)
{
    public static readonly TimeSpan MaxShiftLength = TimeSpan.FromHours(24);
    public const int MinReasonLength = 5;

    public async Task<AttendanceCorrection> CreateAsync(
        int adminUserId, CorrectionRequest req, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length < MinReasonLength || reason.Length > 300)
            throw Invalid($"A reason of {MinReasonLength}-300 characters is required.");

        var employeeExists = await db.Users.AnyAsync(u => u.Id == req.UserId && u.Role == UserRole.Employee, ct);
        if (!employeeExists) throw new CorrectionException(CorrectionError.NotFound, "Employee not found.");

        // One transaction + per-user lock: the overlap check and the insert are atomic with respect to
        // this user's punches and other corrections.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserLock.AcquireAsync(db, req.UserId, ct);

        var effective = await EffectiveAttendance.LoadAsync(db, req.UserId, ct: ct);
        EffectiveRecord? target = null;
        DateTimeOffset newIn;
        DateTimeOffset? newOut;

        if (req.Kind == CorrectionKind.Add)
        {
            if (req.AttendanceRecordId is not null) throw Invalid("An added record must not reference an existing record.");
            if (req.ClockInUtc is null || req.ClockOutUtc is null) throw Invalid("An added record needs both clock-in and clock-out.");
            newIn = req.ClockInUtc.Value;
            newOut = req.ClockOutUtc;
        }
        else
        {
            // Must name a punch: without this, "null == null" would also match manual (Add) entries.
            if (req.AttendanceRecordId is null) throw Invalid("An adjustment must reference an attendance record.");
            target = effective.FirstOrDefault(e => e.RecordId == req.AttendanceRecordId)
                ?? throw new CorrectionException(CorrectionError.NotFound, "Attendance record not found for this employee.");
            // Missing side = keep the current effective value (e.g. only supply a clock-out to close a forgotten one).
            newIn = req.ClockInUtc ?? target.In;
            newOut = req.ClockOutUtc ?? target.Out;
            if (newIn == target.In && newOut == target.Out) throw Invalid("The correction does not change anything.");
        }

        if (newOut is { } o)
        {
            if (o <= newIn) throw Invalid("Clock-out must be after clock-in.");
            if (o - newIn > MaxShiftLength) throw Invalid("A shift cannot be longer than 24 hours.");
            if (o > nowUtc) throw Invalid(FutureMessage("Clock-out", o, nowUtc));
        }
        if (newIn > nowUtc) throw Invalid(FutureMessage("Clock-in", newIn, nowUtc));

        // Must not overlap any OTHER effective record of this employee (open records count as [in, infinity)).
        var clash = effective
            .Where(e => e != target)
            .FirstOrDefault(e => EffectiveAttendance.Overlaps(newIn, newOut, e.In, e.Out));
        if (clash is not null) throw Invalid($"The times overlap {clash.Label}.");

        var correction = new AttendanceCorrection
        {
            Kind = req.Kind,
            AttendanceRecordId = target?.RecordId,
            UserId = req.UserId,
            WorkDate = ZurichTime.LocalDateOf(newIn),
            CorrectedClockInUtc = newIn,
            CorrectedClockOutUtc = newOut,
            PreviousClockInUtc = target?.In,
            PreviousClockOutUtc = target?.Out,
            Reason = reason,
            CreatedByUserId = adminUserId,
            CreatedAtUtc = nowUtc,
        };
        db.AttendanceCorrections.Add(correction);
        await db.SaveChangesAsync(ct);

        // Closing a forgotten clock-out frees the employee to clock in again.
        if (target is { IsOpen: true } && newOut is not null)
        {
            await db.AttendanceRecords
                .Where(r => r.Id == target.RecordId && r.IsOpen)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsOpen, false), ct);
        }

        await tx.CommitAsync(ct);
        return correction;
    }

    // Names both times in Zurich wall-clock time so the admin sees exactly what was compared.
    private static string FutureMessage(string what, DateTimeOffset entered, DateTimeOffset nowUtc) =>
        $"{what} {Zurich(entered)} is in the future. The current Zurich time is {Zurich(nowUtc)}; enter a time at or before it.";

    private static string Zurich(DateTimeOffset utc) => ZurichTime.ToLocal(utc).ToString("yyyy-MM-dd HH:mm");

    private static CorrectionException Invalid(string message) => new(CorrectionError.Invalid, message);
}
