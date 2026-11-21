namespace ClockApp.Attendance.Entities;

/// <summary>
/// One version of an employee's schedule (append-only): expected minutes per weekday plus the overnight setting,
/// effective from a date. A change is a new row, never an update, so past months always recompute with the
/// version that applied to them.
/// </summary>
public class EmployeeScheduleVersion
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    public short ExpectedMinutesMon { get; set; }
    public short ExpectedMinutesTue { get; set; }
    public short ExpectedMinutesWed { get; set; }
    public short ExpectedMinutesThu { get; set; }
    public short ExpectedMinutesFri { get; set; }
    public short ExpectedMinutesSat { get; set; }
    public short ExpectedMinutesSun { get; set; }

    /// <summary>When true a later-date clock-out within 24 h counts automatically (night workers).</summary>
    public bool AllowsOvernightShifts { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
