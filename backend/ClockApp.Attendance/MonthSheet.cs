using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;

namespace ClockApp.Attendance;

/// <summary>One calendar day of the employee's timesheet (one consolidated effective shift).</summary>
public sealed record SheetDay(
    DateOnly Date, DayOfWeek Weekday,
    DateTimeOffset? ClockIn, DateTimeOffset? ClockOut,
    bool ClockInIsManual, bool ClockOutIsManual,
    bool InProgress, bool NeedsCorrection,
    long? WorkedMinutes, int ExpectedMinutes, long? BalanceMinutes,
    bool IsToday, bool IsFuture);

public sealed record SheetTotals(long WorkedMinutes, long ExpectedMinutes, long BalanceMinutes, long WholeMonthExpectedMinutes);

public sealed record MonthSheetResult(int Year, int Month, IReadOnlyList<SheetDay> Days, SheetTotals MonthToDate);

/// <summary>
/// The timesheet of a month, computed from EFFECTIVE records (punches with corrections applied).
///  - A day's shift = first effective clock-in to last effective clock-out; breaks count as worked.
///  - Expected = the schedule version applicable to the date (0 before the employment start date).
///  - Unfinished work is never counted: an open interval, or a closed punch whose clock-out lies on a later Zurich
///    date (unless the schedule allows overnight shifts) or beyond 24 h, "needs correction" until a correction sets
///    the end. Such days have no Worked/Balance and stay out of the totals; the raw punch is untouched.
///  - Future days show Expected only; today shows Worked/Balance only once its shift is finished.
/// No clock is read: "today" is a parameter (the Zurich date supplied by the API layer).
/// </summary>
public static class MonthSheet
{
    public static readonly TimeSpan MaxShift = TimeSpan.FromHours(24);

    public static MonthSheetResult Build(
        int year, int month, IReadOnlyList<EffectiveRecord> effective, IReadOnlyList<EmployeeScheduleVersion> versions,
        DateOnly? employmentStart, DateOnly today)
    {
        var days = new List<SheetDay>();
        long worked = 0, expectedToDate = 0, wholeMonth = 0;

        for (var d = new DateOnly(year, month, 1); d.Month == month; d = d.AddDays(1))
        {
            var expected = ScheduleResolver.ExpectedMinutes(versions, d, employmentStart);
            wholeMonth += expected;
            var isFuture = d > today;
            var isToday = d == today;
            var intervals = effective.Where(r => !r.IsVoided && r.WorkDate == d).OrderBy(r => r.In).ToList();
            var overnightAllowed = ScheduleResolver.VersionFor(versions, d)?.AllowsOvernightShifts ?? false;

            if (intervals.Count == 0)
            {
                // No shift: future shows Expected only; a past expected day is a deficit; a non-working day shows nothing.
                long? balance = !isFuture && !isToday && expected > 0 ? -expected : null;
                if (balance is not null) expectedToDate += expected;
                days.Add(new SheetDay(d, d.DayOfWeek, null, null, false, false, false, false, null, expected, balance, isToday, isFuture));
                continue;
            }

            var first = intervals[0];
            var open = intervals.Any(r => r.Out is null);
            var needsCorrection = intervals.Any(r => NeedsCorrection(r, overnightAllowed));
            var closed = intervals.Where(r => r.Out is not null).ToList();
            var last = closed.OrderBy(r => r.Out).LastOrDefault();

            var unresolved = open || needsCorrection;
            DateTimeOffset? clockOut = unresolved ? null : last?.Out;
            long? workedDay = unresolved || last is null ? null : (long)(last.Out!.Value - first.In).TotalMinutes;
            // Future dates and today-before-clock-out show no Worked/Balance.
            if (isFuture) workedDay = null;
            long? dayBalance = workedDay is { } w ? w - expected : null;

            if (workedDay is { } wd && !isFuture)
            {
                worked += wd;
                expectedToDate += expected;
            }

            days.Add(new SheetDay(d, d.DayOfWeek, first.In, clockOut,
                first.IsManual || (first.IsAdjusted && first.In != first.OriginalIn),
                last is not null && !unresolved && (last.IsManual || (last.IsAdjusted && last.Out != last.OriginalOut)),
                open, needsCorrection, workedDay, expected, dayBalance, isToday, isFuture));
        }

        return new MonthSheetResult(year, month, days, new SheetTotals(worked, expectedToDate, worked - expectedToDate, wholeMonth));
    }

    /// <summary>
    /// A closed punch whose end falls on a later Zurich date (unless the schedule allows overnight shifts) or beyond
    /// 24 h counts for nothing until a correction sets the end. A manual/corrected end is the explicit confirmation.
    /// </summary>
    public static bool NeedsCorrection(EffectiveRecord r, bool overnightAllowed)
    {
        if (r.Out is not { } end) return false;                                        // open: handled as "in progress"
        var endConfirmed = r.IsManual || (r.IsAdjusted && r.Out != r.OriginalOut);     // set by a correction
        if (endConfirmed) return end - r.In > MaxShift;                                // corrected end still beyond the cap
        var laterDate = ZurichTime.LocalDateOf(end) != r.WorkDate;
        return end - r.In > MaxShift || (laterDate && !overnightAllowed);
    }
}
