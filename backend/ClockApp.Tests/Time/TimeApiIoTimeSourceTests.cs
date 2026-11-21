using System.Net;
using ClockApp.Api.Time;
using ClockApp.Attendance.Time;

namespace ClockApp.Tests.Time;

public class TimeApiIoTimeSourceTests
{
    // Shape of a real https://timeapi.io/api/time/current/zone?timeZone=Europe/Zurich response.
    private const string RealResponse =
        """{"year":2026,"month":11,"day":20,"hour":18,"minute":20,"seconds":57,"milliSeconds":266,"dateTime":"2026-11-20T18:20:57.2664359","date":"11/20/2026","time":"18:20","timeZone":"Europe/Zurich","dayOfWeek":"Friday","dstActive":false}""";

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request, Requests.Count));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (TimeApiIoTimeSource Source, StubHandler Handler) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://timeapi.test/") };
        return (new TimeApiIoTimeSource(http), handler);
    }

    [Fact]
    public async Task Converts_a_real_response_to_a_utc_instant()
    {
        var (source, handler) = Create((_, _) => Json(RealResponse));

        var time = await source.GetNowAsync();

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 17, 20, 57, 266, TimeSpan.Zero).AddTicks(4359), time.UtcInstant);
        Assert.Equal(new DateOnly(2026, 11, 20), time.LocalDate);
        Assert.Equal("timeapi.io", time.Provider);
        Assert.Contains("timeZone=Europe/Zurich", handler.Requests.Single().Query);
    }

    [Fact]
    public async Task Recorded_time_comes_from_the_api_not_from_the_machine_clock()
    {
        // A response far from "now": the result must follow the API, whatever this machine's clock says.
        var (source, _) = Create((_, _) => Json("""{"dateTime":"2031-07-04T09:00:00","timeZone":"Europe/Zurich","dstActive":true}"""));

        var time = await source.GetNowAsync();

        Assert.Equal(new DateTimeOffset(2031, 7, 4, 7, 0, 0, TimeSpan.Zero), time.UtcInstant);
    }

    [Fact]
    public async Task Makes_a_single_attempt_and_fails_closed_on_502()
    {
        // Retries and provider fallback belong to ResilientTimeSource; the adapter itself is one attempt.
        var (source, handler) = Create((_, _) => Json("", HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<TimeSourceException>(() => source.GetNowAsync());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fails_closed_when_the_network_is_down()
    {
        var (source, _) = Create((_, _) => throw new HttpRequestException("down"));

        await Assert.ThrowsAsync<TimeSourceException>(() => source.GetNowAsync());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"dateTime":"2026-11-20T18:20:57","timeZone":"Europe/Zurich"}""")]                    // no dstActive
    [InlineData("""{"dateTime":"2026-11-20T18:20:57","timeZone":"America/New_York","dstActive":false}""")] // wrong zone
    [InlineData("""{"dateTime":"2026-07-04T09:00:00","timeZone":"Europe/Zurich","dstActive":false}""")]   // DST contradicts zone rules
    [InlineData("""{"dateTime":"2026-03-29T02:30:00","timeZone":"Europe/Zurich","dstActive":true}""")]    // nonexistent local time
    [InlineData("""{"dateTime":"2026-11-20T17:20:57Z","timeZone":"Europe/Zurich","dstActive":false}""")]  // UTC designator
    [InlineData("""{"dateTime":"2026-11-20T18:20:57+01:00","timeZone":"Europe/Zurich","dstActive":false}""")] // explicit offset
    public async Task Rejects_invalid_or_inconsistent_responses(string body)
    {
        var (source, _) = Create((_, _) => Json(body));

        await Assert.ThrowsAsync<TimeSourceException>(() => source.GetNowAsync());
    }
}
