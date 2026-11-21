namespace ClockApp.Attendance.Entities;

/// <summary>
/// Single-row table (Id is always 1): the company default expected duration for EACH weekday (0 = not a working day).
/// It only prefills new employees; every employee's own schedule is independent. <see cref="DurationMinutes"/> is the
/// legacy single value (kept in sync with the first working day for the old month report).
/// </summary>
public class ShiftSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int DurationMinutes { get; set; }
    public short ExpectedMinutesMon { get; set; }
    public short ExpectedMinutesTue { get; set; }
    public short ExpectedMinutesWed { get; set; }
    public short ExpectedMinutesThu { get; set; }
    public short ExpectedMinutesFri { get; set; }
    public short ExpectedMinutesSat { get; set; }
    public short ExpectedMinutesSun { get; set; }
    public int? UpdatedByUserId { get; set; }

    /// <summary>Monday..Sunday.</summary>
    public int[] Week => [ExpectedMinutesMon, ExpectedMinutesTue, ExpectedMinutesWed, ExpectedMinutesThu, ExpectedMinutesFri, ExpectedMinutesSat, ExpectedMinutesSun];

    public void SetWeek(int[] m)
    {
        ExpectedMinutesMon = (short)m[0]; ExpectedMinutesTue = (short)m[1]; ExpectedMinutesWed = (short)m[2]; ExpectedMinutesThu = (short)m[3];
        ExpectedMinutesFri = (short)m[4]; ExpectedMinutesSat = (short)m[5]; ExpectedMinutesSun = (short)m[6];
        DurationMinutes = m.First(x => x > 0);
    }
}
