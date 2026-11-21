namespace ClockApp.Attendance.Time;

/// <summary>
/// The ONLY source of time for Clock In/Out records: an external Europe/Zurich time API.
/// Implementations must never fall back to the browser or server clock.
/// </summary>
public interface IAttendanceTimeSource
{
    /// <exception cref="TimeSourceException">The API is unreachable or returned an invalid/inconsistent time.</exception>
    Task<AttendanceTime> GetNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>A time obtained from the external API, normalised to a UTC instant plus Zurich local data.</summary>
/// <param name="UtcInstant">The instant, in UTC (stored as truth).</param>
/// <param name="LocalDateTime">Europe/Zurich wall-clock time as reported by the API.</param>
/// <param name="UtcOffset">Zurich offset at that instant (+01:00 or +02:00).</param>
/// <param name="IsDst">Whether daylight saving time was active.</param>
/// <param name="Provider">Name of the API that supplied the time.</param>
public sealed record AttendanceTime(
    DateTimeOffset UtcInstant,
    DateTime LocalDateTime,
    TimeSpan UtcOffset,
    bool IsDst,
    string Provider)
{
    /// <summary>Europe/Zurich local date (used as WorkDate for clock-ins).</summary>
    public DateOnly LocalDate => DateOnly.FromDateTime(LocalDateTime);
}

public class TimeSourceException(string message, Exception? inner = null) : Exception(message, inner);
