namespace ClockApp.Attendance.Entities;

/// <summary>
/// A genuine Clock In/Out event. Times come only from the external Europe/Zurich time API
/// and are never edited afterwards; admin changes are stored as <see cref="AttendanceCorrection"/>.
/// </summary>
public class AttendanceRecord
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset ClockInUtc { get; set; }
    public DateTimeOffset? ClockOutUtc { get; set; }

    /// <summary>Europe/Zurich local date of the clock-in.</summary>
    public DateOnly WorkDate { get; set; }

    /// <summary>Which external API supplied the time.</summary>
    public string TimeProvider { get; set; } = "";

    /// <summary>True until clock-out or until an admin correction closes the record.</summary>
    public bool IsOpen { get; set; }
}
