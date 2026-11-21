using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClockApp.Attendance.Time;

namespace ClockApp.Api.Time;

/// <summary>
/// Gets the current Europe/Zurich time from https://timeapi.io. Fails closed: any problem raises
/// <see cref="TimeSourceException"/>; there is no fallback to the browser or server clock.
/// </summary>
public sealed class TimeApiIoTimeSource(HttpClient http) : IAttendanceTimeSource
{
    public const string ProviderName = "timeapi.io";

    private sealed record Response(
        [property: JsonPropertyName("dateTime")] DateTime? DateTime,
        [property: JsonPropertyName("timeZone")] string? TimeZone,
        [property: JsonPropertyName("dstActive")] bool? DstActive);

    /// <summary>One attempt. Retries and provider fallback are decided by <see cref="ResilientTimeSource"/>.</summary>
    public Task<AttendanceTime> GetNowAsync(CancellationToken cancellationToken = default) => FetchAsync(cancellationToken);

    private async Task<AttendanceTime> FetchAsync(CancellationToken ct)
    {
        Response? body;
        try
        {
            using var response = await http.GetAsync($"api/time/current/zone?timeZone={ZurichTime.ZoneId}", ct);
            if (!response.IsSuccessStatusCode)
                throw new TimeSourceException($"{ProviderName} returned HTTP {(int)response.StatusCode}.");
            body = await response.Content.ReadFromJsonAsync<Response>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new TimeSourceException($"{ProviderName} is unavailable or returned invalid data.", ex);
        }

        if (body?.DateTime is null || body.DstActive is null)
            throw new TimeSourceException($"{ProviderName} response is missing dateTime or dstActive.");
        if (!string.Equals(body.TimeZone, ZurichTime.ZoneId, StringComparison.Ordinal))
            throw new TimeSourceException($"{ProviderName} returned time zone '{body.TimeZone}', expected {ZurichTime.ZoneId}.");

        // timeapi.io sends zone-local time without an offset. A Z/offset would be silently converted by the
        // JSON parser (to UTC or the server's zone) and then misread as Zurich local time, so reject it.
        if (body.DateTime.Value.Kind != DateTimeKind.Unspecified)
            throw new TimeSourceException($"{ProviderName} dateTime unexpectedly carries a UTC/offset designator.");

        return ZurichTime.FromLocal(body.DateTime.Value, body.DstActive.Value, ProviderName);
    }
}
