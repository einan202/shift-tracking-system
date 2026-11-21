using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

public enum ClockOutcome
{
    /// <summary>The punch was recorded.</summary>
    Recorded,
    /// <summary>Clock-in while a record is already open.</summary>
    AlreadyClockedIn,
    /// <summary>Clock-out with no open record (never clocked in, or someone else already clocked out).</summary>
    NotClockedIn,
    /// <summary>The API time falls inside / before an existing (e.g. admin-corrected) record of this employee.</summary>
    OverlapsExisting,
    /// <summary>Clock-out time is not after the clock-in an admin CORRECTED (a data conflict, not a time-provider fault).</summary>
    ConflictsWithCorrection,
    /// <summary>Clock-in on a day the employee's schedule does not include (e.g. the weekend). Clock-out is never blocked.</summary>
    NonWorkingDay,
}

public sealed record ClockResult(ClockOutcome Outcome, AttendanceRecord? Record, DateTimeOffset? CorrectedClockIn = null);

/// <summary>
/// Clock In / Clock Out (missing/duplicate punch policy "A": strict reject).
///
/// CLOCK RULE: the recorded time comes exclusively from <see cref="IAttendanceTimeSource"/> (the external
/// Europe/Zurich API). This class has no other time input: no parameters, no TimeProvider, no system clock
/// (the banned-API analyzer enforces the latter at build time). If the API fails, <see cref="TimeSourceException"/>
/// propagates and NOTHING is written.
/// </summary>
public sealed class AttendanceService(AttendanceDbContext db, IAttendanceTimeSource timeSource)
{
    public async Task<ClockResult> ClockInAsync(int userId, CancellationToken ct = default)
    {
        // Cheap early exit: avoids calling the time API when the answer is already known.
        if (await db.AttendanceRecords.AnyAsync(r => r.UserId == userId && r.IsOpen, ct))
            return new ClockResult(ClockOutcome.AlreadyClockedIn, null);

        // Network I/O happens BEFORE touching the database, so no locks/transactions are held while waiting.
        var now = await timeSource.GetNowAsync(ct);

        // Only working days of the applicable schedule version can be started (clock-out is never blocked, so an
        // open shift can always be closed). Employees without any schedule version are not restricted.
        var versions = await db.EmployeeScheduleVersions.AsNoTracking().Where(v => v.UserId == userId).ToListAsync(ct);
        if (versions.Count > 0)
        {
            var start = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmploymentStartDate).SingleAsync(ct);
            if (ScheduleResolver.ExpectedMinutes(versions, now.LocalDate, start) == 0)
                return new ClockResult(ClockOutcome.NonWorkingDay, null);
        }

        // Serialise with admin corrections / other punches of this user (see UserLock).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserLock.AcquireAsync(db, userId, ct);

        if (await db.AttendanceRecords.AnyAsync(r => r.UserId == userId && r.IsOpen, ct))
            return new ClockResult(ClockOutcome.AlreadyClockedIn, null);

        // A new shift starts "now" and is open-ended, so it clashes with any effective record that ends after now
        // (possible only through a correction that was made in between).
        var effective = await EffectiveAttendance.LoadAsync(db, userId, ct: ct);
        if (effective.Any(e => e.Out is null || e.Out > now.UtcInstant))
            return new ClockResult(ClockOutcome.OverlapsExisting, null);

        var record = new AttendanceRecord
        {
            UserId = userId,
            ClockInUtc = now.UtcInstant,
            WorkDate = now.LocalDate,       // Zurich local date of the clock-in
            TimeProvider = now.Provider,
            IsOpen = true,
        };
        db.AttendanceRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Safety net: the filtered unique index UX_OneOpenPerUser also guarantees one open record per user.
            db.Entry(record).State = EntityState.Detached;
            return new ClockResult(ClockOutcome.AlreadyClockedIn, null);
        }
        return new ClockResult(ClockOutcome.Recorded, record);
    }

    public async Task<ClockResult> ClockOutAsync(int userId, CancellationToken ct = default)
    {
        if (!await db.AttendanceRecords.AnyAsync(r => r.UserId == userId && r.IsOpen, ct))
            return new ClockResult(ClockOutcome.NotClockedIn, null);

        var now = await timeSource.GetNowAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserLock.AcquireAsync(db, userId, ct);

        var open = (await EffectiveAttendance.LoadAsync(db, userId, ct: ct)).FirstOrDefault(e => e.IsOpen);
        if (open is null) return new ClockResult(ClockOutcome.NotClockedIn, null);

        // Provider time not after the GENUINE clock-in: the time source is inconsistent -> fail closed (503).
        if (now.UtcInstant <= open.OriginalIn!.Value)
            throw new TimeSourceException("Time source returned a time that is not after the clock-in; nothing was recorded.");

        // Provider time is fine, but an admin corrected the clock-in to a later time (up to the server's "now",
        // and the provider can be a little behind it). That is a conflict with the corrected data -> 409, nothing written.
        if (now.UtcInstant <= open.In)
            return new ClockResult(ClockOutcome.ConflictsWithCorrection, null, open.In);

        // Atomic conditional update: the first committed clock-out wins and is never overwritten.
        // A concurrent second one matches 0 rows and becomes a 409 (its API time is discarded).
        // "ClockInUtc < out" also guarantees the shift has positive length (provider skew guard).
        var rows = await db.AttendanceRecords
            .Where(r => r.UserId == userId && r.IsOpen && r.ClockInUtc < now.UtcInstant)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ClockOutUtc, now.UtcInstant)
                .SetProperty(r => r.IsOpen, false), ct);

        if (rows == 1)
        {
            var saved = await db.AttendanceRecords.AsNoTracking()
                .Where(r => r.UserId == userId).OrderByDescending(r => r.ClockInUtc).FirstAsync(ct);
            await tx.CommitAsync(ct);
            return new ClockResult(ClockOutcome.Recorded, saved);
        }

        // 0 rows: either someone else just clocked out, or the API time is not after the clock-in.
        if (await db.AttendanceRecords.AsNoTracking().AnyAsync(r => r.UserId == userId && r.IsOpen, ct))
            throw new TimeSourceException("Time source returned a time that is not after the clock-in; nothing was recorded.");
        return new ClockResult(ClockOutcome.NotClockedIn, null);
    }

    /// <summary>
    /// The source of truth the UI re-reads after any failure/reload: the open record, or else the latest one,
    /// as EFFECTIVE values (admin corrections applied), never the original punch alone.
    /// </summary>
    public async Task<EffectiveRecord?> GetCurrentOrLastAsync(int userId, CancellationToken ct = default) =>
        (await EffectiveAttendance.LoadAsync(db, userId, ct: ct))
            .OrderByDescending(e => e.IsOpen).ThenByDescending(e => e.In)
            .FirstOrDefault();

    /// <summary>One punch as effective values (used for the response to the punch that was just recorded).</summary>
    public async Task<EffectiveRecord?> GetEffectiveAsync(int userId, long recordId, CancellationToken ct = default) =>
        (await EffectiveAttendance.LoadAsync(db, userId, ct: ct)).FirstOrDefault(e => e.RecordId == recordId);

    // SQL Server error numbers for unique index / constraint violations.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
