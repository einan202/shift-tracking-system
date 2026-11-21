namespace ClockApp.Attendance;

public sealed record ReportEntry(DateTimeOffset In, DateTimeOffset? Out, bool IsManual, bool IsAdjusted, bool IsOpen);

public sealed record ReportDay(
    DateOnly Date, IReadOnlyList<ReportEntry> Records, long WorkedMinutes, long? DifferenceMinutes, bool HasOpenRecord);

public sealed record ReportTotals(long WorkedMinutes, long ExpectedMinutes, long DifferenceMinutes, int DaysWorked);

public sealed record MonthlyReportResult(int Year, int Month, int DefaultShiftMinutes, IReadOnlyList<ReportDay> Days, ReportTotals Totals);

/// <summary>
/// Pure calculation of an employee's month from EFFECTIVE records (corrections already applied).
/// - A record belongs to the Zurich date of its clock-in (WorkDate), so a night shift counts on the day it
///   started and a shift started on the last day of a month stays in that month.
/// - Duration is the UTC difference, so 23 h / 25 h daylight-saving days are correct.
/// - Open records (no clock-out) are listed but excluded from the totals.
/// - Expected time = (days with at least one completed record) x default shift duration.
/// No clock is read here; the month is chosen by the caller.
/// </summary>
public static class MonthlyReport
{
    public static MonthlyReportResult Build(IEnumerable<EffectiveRecord> records, int defaultShiftMinutes, int year, int month)
    {
        var days = records
            .Where(r => r.WorkDate.Year == year && r.WorkDate.Month == month)
            .GroupBy(r => r.WorkDate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var entries = g.OrderBy(r => r.In)
                    .Select(r => new ReportEntry(r.In, r.Out, r.IsManual, r.IsAdjusted, r.Out is null))
                    .ToList();
                var closed = g.Where(r => r.Out is not null).ToList();
                var worked = (long)closed.Aggregate(TimeSpan.Zero, (sum, r) => sum + (r.Out!.Value - r.In)).TotalMinutes;
                long? diff = closed.Count > 0 ? worked - defaultShiftMinutes : null;
                return new ReportDay(g.Key, entries, worked, diff, entries.Any(e => e.IsOpen));
            })
            .ToList();

        var daysWorked = days.Count(d => d.DifferenceMinutes is not null);
        var worked = days.Sum(d => d.WorkedMinutes);
        var expected = (long)daysWorked * defaultShiftMinutes;
        return new MonthlyReportResult(year, month, defaultShiftMinutes, days,
            new ReportTotals(worked, expected, worked - expected, daysWorked));
    }
}
