using ClockApp.Api.Time;
using ClockApp.Attendance.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClockApp.Tests.Time;

public class ResilientTimeSourceTests
{
    private sealed class Fake(string provider, params bool[] succeedOnCall) : IAttendanceTimeSource
    {
        public int Calls;
        public Task<AttendanceTime> GetNowAsync(CancellationToken ct = default)
        {
            var ok = Calls < succeedOnCall.Length && succeedOnCall[Calls];
            Calls++;
            if (!ok) throw new TimeSourceException($"{provider} failed");
            return Task.FromResult(ZurichTime.FromLocal(new DateTime(2026, 11, 20, 18, 20, 57), false, provider));
        }
    }

    private static ResilientTimeSource Create(Fake primary, Fake fallback) =>
        new([(primary, 2), (fallback, 2)], NullLogger<ResilientTimeSource>.Instance, TimeSpan.Zero);

    [Fact]
    public async Task Uses_the_primary_and_never_touches_the_fallback_when_it_works()
    {
        var p = new Fake("primary", true); var f = new Fake("fallback", true);

        var t = await Create(p, f).GetNowAsync();

        Assert.Equal("primary", t.Provider);
        Assert.Equal(1, p.Calls);
        Assert.Equal(0, f.Calls);
    }

    [Fact]
    public async Task Retries_the_primary_once_before_falling_back()
    {
        var p = new Fake("primary", false, true); var f = new Fake("fallback", true);

        var t = await Create(p, f).GetNowAsync();

        Assert.Equal("primary", t.Provider);
        Assert.Equal(2, p.Calls);
        Assert.Equal(0, f.Calls);
    }

    [Fact]
    public async Task Falls_back_when_the_primary_keeps_failing_and_records_the_fallback_provider()
    {
        var p = new Fake("primary"); var f = new Fake("fallback", true);

        var t = await Create(p, f).GetNowAsync();

        Assert.Equal("fallback", t.Provider);
        Assert.Equal(2, p.Calls);
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task Fails_closed_after_a_bounded_number_of_attempts_when_every_provider_fails()
    {
        var p = new Fake("primary"); var f = new Fake("fallback");

        var ex = await Assert.ThrowsAsync<TimeSourceException>(() => Create(p, f).GetNowAsync());

        Assert.Equal(2, p.Calls);
        Assert.Equal(2, f.Calls); // at most 4 requests per punch, then 503: no local clock is ever substituted
        Assert.Contains("primary", ex.Message);
        Assert.Contains("fallback", ex.Message);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var p = new Fake("primary", true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = new ResilientTimeSource([(new CancelThrower(), 2)], NullLogger<ResilientTimeSource>.Instance, TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetNowAsync(cts.Token));
        Assert.Equal(0, p.Calls);
    }

    private sealed class CancelThrower : IAttendanceTimeSource
    {
        public Task<AttendanceTime> GetNowAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); throw new TimeSourceException("x"); }
    }
}
