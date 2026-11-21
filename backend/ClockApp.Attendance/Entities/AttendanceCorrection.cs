namespace ClockApp.Attendance.Entities;

public enum CorrectionKind { Add, Adjust, Void }

/// <summary>Append-only admin correction (the audit trail). Never updated or deleted.</summary>
public class AttendanceCorrection
{
    public long Id { get; set; }
    public CorrectionKind Kind { get; set; }

    /// <summary>Null for <see cref="CorrectionKind.Add"/> (a missing record).</summary>
    public long? AttendanceRecordId { get; set; }

    /// <summary>For Adjust/Void of a manually added interval: the Add correction it refers to (null otherwise).</summary>
    public long? TargetCorrectionId { get; set; }

    /// <summary>Groups the rows created by one employee action.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>One of <see cref="ReasonCodes"/>; free text goes into <see cref="Reason"/>.</summary>
    public string ReasonCode { get; set; } = ReasonCodes.Other;

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

/// <summary>Standard correction reasons (the list the employee picks from). "Other" requires free text in the reason.</summary>
public static class ReasonCodes
{
    public const string ForgotClockIn = "ForgotClockIn";
    public const string ForgotClockOut = "ForgotClockOut";
    public const string WrongTimePunched = "WrongTimePunched";
    public const string DuplicateOrAccidental = "DuplicateOrAccidental";
    public const string WorkedOffsite = "WorkedOffsite";
    public const string SystemProblem = "SystemProblem";
    public const string Other = "Other";

    public static readonly string[] All =
        [ForgotClockIn, ForgotClockOut, WrongTimePunched, DuplicateOrAccidental, WorkedOffsite, SystemProblem, Other];
}
