namespace ClockApp.Attendance.Entities;

/// <summary>Single-row table (Id is always 1): the default shift is defined by duration only.</summary>
public class ShiftSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int DurationMinutes { get; set; }
    public int? UpdatedByUserId { get; set; }
}
