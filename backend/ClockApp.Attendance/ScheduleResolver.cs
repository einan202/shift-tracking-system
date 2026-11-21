using ClockApp.Attendance.Entities;

namespace ClockApp.Attendance;

/// <summary>
/// Which schedule applies to a date. Schedules are effective-dated and append-only, so every date is calculated
/// with the version that applied to it and a later change never alters past months.
/// </summary>
public static class ScheduleResolver
{
    /// <summary>
    /// The version with the greatest EffectiveFrom not later than the date (ties: newest). A date earlier than every
    /// version uses the earliest version. Null when the employee has no version at all.
    /// </summary>
    public static EmployeeScheduleVersion? VersionFor(IEnumerable<EmployeeScheduleVersion> versions, DateOnly date)
    {
        var ordered = versions.OrderBy(v => v.EffectiveFrom).ThenBy(v => v.Id).ToList();
        if (ordered.Count == 0) return null;
        return ordered.LastOrDefault(v => v.EffectiveFrom <= date) ?? ordered[0];
    }

    /// <summary>Expected minutes for a date: 0 before the employment start date, else the weekday's minutes in the applicable version.</summary>
    public static int ExpectedMinutes(IEnumerable<EmployeeScheduleVersion> versions, DateOnly date, DateOnly? employmentStart)
    {
        if (employmentStart is { } start && date < start) return 0;
        var v = VersionFor(versions, date);
        return v is null ? 0 : MinutesFor(v, date.DayOfWeek);
    }

    public static int MinutesFor(EmployeeScheduleVersion v, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => v.ExpectedMinutesMon,
        DayOfWeek.Tuesday => v.ExpectedMinutesTue,
        DayOfWeek.Wednesday => v.ExpectedMinutesWed,
        DayOfWeek.Thursday => v.ExpectedMinutesThu,
        DayOfWeek.Friday => v.ExpectedMinutesFri,
        DayOfWeek.Saturday => v.ExpectedMinutesSat,
        _ => v.ExpectedMinutesSun,
    };

    /// <summary>Minutes Monday..Sunday (the order the API uses).</summary>
    public static int[] Weekdays(EmployeeScheduleVersion v) =>
        [v.ExpectedMinutesMon, v.ExpectedMinutesTue, v.ExpectedMinutesWed, v.ExpectedMinutesThu, v.ExpectedMinutesFri, v.ExpectedMinutesSat, v.ExpectedMinutesSun];

    public static void Apply(EmployeeScheduleVersion v, int[] minutes)
    {
        v.ExpectedMinutesMon = (short)minutes[0]; v.ExpectedMinutesTue = (short)minutes[1]; v.ExpectedMinutesWed = (short)minutes[2];
        v.ExpectedMinutesThu = (short)minutes[3]; v.ExpectedMinutesFri = (short)minutes[4];
        v.ExpectedMinutesSat = (short)minutes[5]; v.ExpectedMinutesSun = (short)minutes[6];
    }

    /// <summary>Null when valid, else the problem.</summary>
    public static string? Validate(int[]? minutes)
    {
        if (minutes is not { Length: 7 }) return "Give the expected minutes for all seven weekdays (Monday to Sunday).";
        if (minutes.Any(m => m is < 0 or > 1440)) return "Each weekday's expected duration must be between 0 and 24 hours.";
        if (minutes.All(m => m == 0)) return "Select at least one working weekday with a duration.";
        return null;
    }
}
