using System.Net;
using ClockApp.Api.Time;
using ClockApp.Attendance.Time;

namespace ClockApp.Tests.Time;

public class TimeNowTimeSourceTests
{
    // Shape of a real https://time.now/developer/api/timezone/Europe/Zurich response (trimmed).
    private const string Real =
        """{"abbreviation":"CET","datetime":"2026-11-21T13:33:29.535217+01:00","dst":false,"raw_offset":3600,"timezone":"Europe/Zurich","unixtime":1795264409,"utc_datetime":"2026-11-21T12:33:29.535217Z","utc_offset":"+01:00"}""";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static TimeNowTimeSource Create(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new TimeNowTimeSource(new HttpClient(handler) { BaseAddress = new Uri("https://time.test/") });
    }

    [Fact]
    public async Task Converts_a_real_response_to_the_utc_instant_with_zurich_local_data()
    {
        var source = Create(_ => Json(Real), out _);

        var t = await source.GetNowAsync();

        Assert.Equal(DateTimeOffset.Parse("2026-11-21T12:33:29.535217Z"), t.UtcInstant);
        Assert.Equal(TimeSpan.FromHours(1), t.UtcOffset);
        Assert.False(t.IsDst);
        Assert.Equal(new DateOnly(2026, 11, 21), t.LocalDate);
        Assert.Equal("time.now", t.Provider);
    }

    [Fact]
    public async Task Requests_the_europe_zurich_endpoint()
    {
        Uri? seen = null;
        var source = Create(r => { seen = r.RequestUri; return Json(Real); }, out _);

        await source.GetNowAsync();

        Assert.Equal("/developer/api/timezone/Europe/Zurich", seen!.AbsolutePath);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"utc_datetime":"2026-11-21T12:33:29Z","utc_offset":"+01:00","dst":false,"timezone":"America/New_York","unixtime":1795264409}""")]       // wrong zone
    [InlineData("""{"utc_datetime":"2026-07-04T07:00:00Z","utc_offset":"+02:00","dst":false,"timezone":"Europe/Zurich","unixtime":1783148400}""")]         // dst contradicts zone rules
    [InlineData("""{"utc_datetime":"2026-07-04T07:00:00Z","utc_offset":"+01:00","dst":true,"timezone":"Europe/Zurich","unixtime":1783148400}""")]          // offset contradicts zone rules
    [InlineData("""{"utc_datetime":"2026-11-21T12:33:29Z","utc_offset":"+01:00","dst":false,"timezone":"Europe/Zurich","unixtime":1700000000}""")]        // unixtime disagrees
    [InlineData("""{"utc_datetime":"2026-11-21T13:33:29+01:00","utc_offset":"+01:00","dst":false,"timezone":"Europe/Zurich","unixtime":1795264409}""")]   // not a UTC timestamp
    [InlineData("""{"utc_datetime":"2026-11-21T12:33:29Z","utc_offset":"+01:00","timezone":"Europe/Zurich","unixtime":1795264409}""")]                    // no dst
    public async Task Rejects_invalid_or_inconsistent_responses(string body)
    {
        var source = Create(_ => Json(body), out _);

        await Assert.ThrowsAsync<TimeSourceException>(() => source.GetNowAsync());
    }

    [Fact]
    public async Task Fails_closed_on_http_errors_and_a_down_network_with_one_attempt()
    {
        var bad = Create(_ => Json("", HttpStatusCode.BadGateway), out var h);
        await Assert.ThrowsAsync<TimeSourceException>(() => bad.GetNowAsync());
        Assert.Equal(1, h.Calls);

        var down = Create(_ => throw new HttpRequestException("down"), out _);
        await Assert.ThrowsAsync<TimeSourceException>(() => down.GetNowAsync());
    }
}
