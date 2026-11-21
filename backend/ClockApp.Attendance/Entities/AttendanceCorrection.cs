namespace ClockApp.Attendance.Entities;

public enum CorrectionKind { Add, Adjust }

/// <summary>Append-only admin correction (the audit trail). Never updated or deleted.</summary>
public class AttendanceCorrection
{
    public long Id { get; set; }
    public CorrectionKind Kind { get; set; }

    /// <summary>Null for <see cref="CorrectionKind.Add"/> (a missing record).</summary>
    public long? AttendanceRecordId { get; set; }

    /// <summary>The employee concerned.</summary>
    public int UserId { get; set; }
    public DateOnly WorkDate { get; set; }

    public DateTimeOffset? CorrectedClockInUtc { get; set; }
    public DateTimeOffset? CorrectedClockOutUtc { get; set; }
    public DateTimeOffset? PreviousClockInUtc { get; set; }
    public DateTimeOffset? PreviousClockOutUtc { get; set; }

    public string Reason { get; set; } = "";
    public int CreatedByUserId { get; set; }

    /// <summary>When the admin made the correction (audit metadata, supplied by the API layer).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
