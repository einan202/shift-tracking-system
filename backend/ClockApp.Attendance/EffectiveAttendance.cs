using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

/// <summary>
/// One row of "what counts": a genuine punch (possibly overridden by its latest correction) or a manually
/// added record. <see cref="OriginalIn"/>/<see cref="OriginalOut"/> are the untouched API-recorded punch times.
/// </summary>
public sealed record EffectiveRecord(
    long? RecordId,
    long? AddCorrectionId,
    DateTimeOffset In,
    DateTimeOffset? Out,
    DateOnly WorkDate,
    bool IsManual,
    bool IsAdjusted,
    bool IsOpen,
    DateTimeOffset? OriginalIn,
    DateTimeOffset? OriginalOut,
    string? Provider = null)   // which time API recorded the punch (null for a manual entry)
{
    public string Label => RecordId is { } r ? $"record #{r}" : $"manual entry #{AddCorrectionId}";
}

public static class EffectiveAttendance
{
    /// <summary>
    /// Effective records of one user = original punches overridden by the LATEST correction of each record,
    /// plus 'Add' corrections. Originals are never modified; this is computed on read.
    /// </summary>
    public static async Task<List<EffectiveRecord>> LoadAsync(
        AttendanceDbContext db, int userId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var records = await db.AttendanceRecords.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(ct);
        var corrections = await db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.UserId == userId).OrderBy(c => c.Id).ToListAsync(ct);

        var latest = corrections.Where(c => c.AttendanceRecordId != null)
            .GroupBy(c => c.AttendanceRecordId!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        var result = new List<EffectiveRecord>();
        foreach (var r in records)
        {
            if (latest.TryGetValue(r.Id, out var c))
            {
                // "?? original" keeps a later genuine clock-out visible when the correction left the end open.
                result.Add(new EffectiveRecord(r.Id, null,
                    c.CorrectedClockInUtc ?? r.ClockInUtc, c.CorrectedClockOutUtc ?? r.ClockOutUtc,
                    c.WorkDate, false, true, r.IsOpen, r.ClockInUtc, r.ClockOutUtc, r.TimeProvider));
            }
            else
            {
                result.Add(new EffectiveRecord(r.Id, null, r.ClockInUtc, r.ClockOutUtc,
                    r.WorkDate, false, false, r.IsOpen, r.ClockInUtc, r.ClockOutUtc, r.TimeProvider));
            }
        }
        foreach (var add in corrections.Where(c => c.Kind == CorrectionKind.Add))
        {
            result.Add(new EffectiveRecord(null, add.Id, add.CorrectedClockInUtc!.Value, add.CorrectedClockOutUtc,
                add.WorkDate, true, false, false, null, null));
        }

        return result
            .Where(e => (from is null || e.WorkDate >= from) && (to is null || e.WorkDate <= to))
            .OrderBy(e => e.In)
            .ToList();
    }

    /// <summary>Half-open interval overlap on [in, out); a null out means "still open" (to infinity). Touching is allowed.</summary>
    public static bool Overlaps(DateTimeOffset aIn, DateTimeOffset? aOut, DateTimeOffset bIn, DateTimeOffset? bOut) =>
        (bOut is null || aIn < bOut) && (aOut is null || bIn < aOut);
}
