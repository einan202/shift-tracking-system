using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClockApp.Attendance.Time;

namespace ClockApp.Api.Time;

/// <summary>
/// Gets the current Europe/Zurich time from https://time.now (WorldTimeAPI-compatible, no API key).
/// Unlike timeapi.io it reports the UTC instant directly, so there is no local-time ambiguity to resolve.
/// Single attempt; fails closed with <see cref="TimeSourceException"/>.
/// </summary>
public sealed class TimeNowTimeSource(HttpClient http) : IAttendanceTimeSource
{
    public const string ProviderName = "time.now";
    private const string Path = "developer/api/timezone/Europe/Zurich";

    private sealed record Response(
        [property: JsonPropertyName("utc_datetime")] string? UtcDateTime,
        [property: JsonPropertyName("utc_offset")] string? UtcOffset,
        [property: JsonPropertyName("dst")] bool? Dst,
        [property: JsonPropertyName("timezone")] string? Timezone,
        [property: JsonPropertyName("unixtime")] long? UnixTime);

    public async Task<AttendanceTime> GetNowAsync(CancellationToken ct = default)
    {
        Response? body;
        try
        {
            using var response = await http.GetAsync(Path, ct);
            if (!response.IsSuccessStatusCode)
                throw new TimeSourceException($"{ProviderName} returned HTTP {(int)response.StatusCode}.");
            body = await response.Content.ReadFromJsonAsync<Response>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new TimeSourceException($"{ProviderName} is unavailable or returned invalid data.", ex);
        }

        if (body is null || body.UtcDateTime is null || body.UtcOffset is null || body.Dst is null || body.UnixTime is null)
            throw new TimeSourceException($"{ProviderName} response is missing required fields.");
        if (!string.Equals(body.Timezone, ZurichTime.ZoneId, StringComparison.Ordinal))
            throw new TimeSourceException($"{ProviderName} returned time zone '{body.Timezone}', expected {ZurichTime.ZoneId}.");

        // The instant must be an explicit UTC value; anything else would be misread.
        if (!DateTimeOffset.TryParse(body.UtcDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc) || utc.Offset != TimeSpan.Zero
            || !(body.UtcDateTime.EndsWith('Z') || body.UtcDateTime.EndsWith("+00:00", StringComparison.Ordinal)))
            throw new TimeSourceException($"{ProviderName} utc_datetime is not a UTC timestamp.");
        if (!TimeSpan.TryParse(body.UtcOffset.TrimStart('+'), CultureInfo.InvariantCulture, out var offset))
            throw new TimeSourceException($"{ProviderName} utc_offset is not parseable.");

        // Two independent fields of the same response must agree (guards against a garbled/cached answer).
        if (Math.Abs(utc.ToUnixTimeSeconds() - body.UnixTime.Value) > 1)
            throw new TimeSourceException($"{ProviderName} utc_datetime and unixtime disagree.");

        return ZurichTime.FromUtc(utc, body.Dst.Value, offset, ProviderName);
    }
}
