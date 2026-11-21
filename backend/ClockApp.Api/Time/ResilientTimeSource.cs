using ClockApp.Attendance.Time;

namespace ClockApp.Api.Time;

/// <summary>
/// The attendance time source: external providers tried in order, each with a small bounded number of
/// attempts. Re-reading the time is a side-effect-free GET that happens BEFORE anything is written, so
/// retrying and falling back are safe: a punch is stored once, with the time of the one successful answer
/// and the name of the provider that supplied it. If every provider fails the request fails (503) and
/// nothing is written: there is no fallback to the browser or server clock.
/// </summary>
public sealed class ResilientTimeSource(
    IReadOnlyList<(IAttendanceTimeSource Source, int Attempts)> providers,
    ILogger<ResilientTimeSource> log,
    TimeSpan? retryDelay = null) : IAttendanceTimeSource
{
    private readonly TimeSpan _delay = retryDelay ?? TimeSpan.FromMilliseconds(150);

    public async Task<AttendanceTime> GetNowAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (var (source, attempts) in providers)
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    var time = await source.GetNowAsync(cancellationToken);
                    if (failures.Count > 0)
                        log.LogWarning("Time obtained from {Provider} after failures: {Failures}", time.Provider, string.Join(" | ", failures));
                    return time;
                }
                catch (TimeSourceException ex)
                {
                    failures.Add($"{source.GetType().Name}#{attempt}: {ex.Message}");
                    if (attempt < attempts) await Task.Delay(_delay, cancellationToken);
                }
            }
        }
        throw new TimeSourceException($"No time provider answered ({providers.Sum(p => p.Attempts)} attempts): {string.Join(" | ", failures)}");
    }
}
