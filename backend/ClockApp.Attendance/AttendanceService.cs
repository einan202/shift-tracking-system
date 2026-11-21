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
}

public sealed record ClockResult(ClockOutcome Outcome, AttendanceRecord? Record);

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
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two simultaneous clock-ins (double click / two tabs): the filtered unique index
            // UX_OneOpenPerUser let exactly one through. The loser is a plain 409, not an error.
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
            return new ClockResult(ClockOutcome.Recorded, saved);
        }

        // 0 rows: either someone else just clocked out, or the API time is not after the clock-in.
        if (await db.AttendanceRecords.AsNoTracking().AnyAsync(r => r.UserId == userId && r.IsOpen, ct))
            throw new TimeSourceException("Time source returned a time that is not after the clock-in; nothing was recorded.");
        return new ClockResult(ClockOutcome.NotClockedIn, null);
    }

    /// <summary>The source of truth the UI re-reads after any failure/reload: open record, or the latest one.</summary>
    public async Task<AttendanceRecord?> GetCurrentOrLastAsync(int userId, CancellationToken ct = default)
    {
        return await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.IsOpen).ThenByDescending(r => r.ClockInUtc)
            .FirstOrDefaultAsync(ct);
    }

    // SQL Server error numbers for unique index / constraint violations.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
