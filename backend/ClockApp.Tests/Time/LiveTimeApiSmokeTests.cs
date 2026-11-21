using ClockApp.Api.Time;

namespace ClockApp.Tests.Time;

/// <summary>Opt-in check against the real API: set RUN_LIVE_TIME_API=1 (needs internet).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_TIME_API") != "1")
            Skip = "Set RUN_LIVE_TIME_API=1 to call the real time API.";
    }
}

public class LiveTimeApiSmokeTests
{
    private static void AssertConsistent(ClockApp.Attendance.Time.AttendanceTime time)
    {
        // Sanity only (not a clock for recording): the API's UTC instant is near this machine's UTC time.
        var drift = (time.UtcInstant - DateTimeOffset.UtcNow).Duration();
        Assert.True(drift < TimeSpan.FromMinutes(2), $"API time differs from machine clock by {drift}.");
        Assert.Contains(time.UtcOffset, new[] { TimeSpan.FromHours(1), TimeSpan.FromHours(2) });
    }

    // The adapters make one attempt; timeapi.io alone answers 502 for about a third of requests, so allow a few tries.
    private static async Task<ClockApp.Attendance.Time.AttendanceTime> WithTries(ClockApp.Attendance.Time.IAttendanceTimeSource source)
    {
        for (var i = 1; ; i++)
        {
            try { return await source.GetNowAsync(); }
            catch (ClockApp.Attendance.Time.TimeSourceException) when (i < 6) { await Task.Delay(300); }
        }
    }

    [LiveFact]
    public async Task Real_time_now_returns_a_consistent_zurich_time()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://time.now/"), Timeout = TimeSpan.FromSeconds(10) };
        AssertConsistent(await WithTries(new TimeNowTimeSource(http)));
    }

    [LiveFact]
    public async Task Real_timeapi_io_returns_a_consistent_zurich_time()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://timeapi.io/"), Timeout = TimeSpan.FromSeconds(10) };
        AssertConsistent(await WithTries(new TimeApiIoTimeSource(http)));
    }
}
