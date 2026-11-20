namespace ClockApp.Attendance.Time;

/// <summary>
/// Converts the local Europe/Zurich time reported by an API (plus its DST flag) into a UTC instant.
/// TimeZoneInfo is used only for the zone's rules (offsets / DST periods), never as a clock.
/// </summary>
public static class ZurichTime
{
    public const string ZoneId = "Europe/Zurich";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    /// <exception cref="TimeSourceException">
    /// The local time does not exist (spring-forward gap) or contradicts the reported DST flag.
    /// </exception>
    public static AttendanceTime FromLocal(DateTime local, bool dstActive, string provider)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(local))
            throw new TimeSourceException($"{local:O} does not exist in {ZoneId} (daylight saving gap).");

        TimeSpan offset;
        if (Zone.IsAmbiguousTime(local))
        {
            // Fall-back hour occurs twice; the API's DST flag says which occurrence this is.
            var offsets = Zone.GetAmbiguousTimeOffsets(local);
            var standard = offsets.Min();
            var daylight = offsets.Max();
            offset = dstActive ? daylight : standard;
        }
        else
        {
            offset = Zone.GetUtcOffset(local);
            if (Zone.IsDaylightSavingTime(local) != dstActive)
                throw new TimeSourceException(
                    $"Time source reported dstActive={dstActive} for {local:O}, which contradicts {ZoneId} rules.");
        }

        var instant = new DateTimeOffset(local, offset).ToUniversalTime();
        return new AttendanceTime(instant, local, offset, dstActive, provider);
    }
}
