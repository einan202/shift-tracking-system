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

    /// <summary>
    /// Converts an admin-typed Zurich wall-clock time to a UTC instant (for corrections, never for punches).
    /// Nonexistent times (spring-forward gap) are rejected; ambiguous times (fall-back hour) need the
    /// UTC offset in minutes (+60 or +120) to say which occurrence is meant.
    /// </summary>
    /// <exception cref="InvalidLocalTimeException"/>
    public static DateTimeOffset LocalToUtc(DateTime local, int? offsetMinutes = null)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(local))
            throw new InvalidLocalTimeException($"{local:yyyy-MM-dd HH:mm} does not exist in Europe/Zurich (clocks skip this hour).");

        TimeSpan offset;
        if (Zone.IsAmbiguousTime(local))
        {
            var options = Zone.GetAmbiguousTimeOffsets(local);
            if (offsetMinutes is null)
                throw new InvalidLocalTimeException(
                    $"{local:yyyy-MM-dd HH:mm} happens twice in Europe/Zurich; specify the UTC offset in minutes (60 or 120).");
            var match = options.Where(o => (int)o.TotalMinutes == offsetMinutes).Cast<TimeSpan?>().FirstOrDefault();
            offset = match ?? throw new InvalidLocalTimeException("The UTC offset must be 60 or 120 minutes for this ambiguous time.");
        }
        else
        {
            offset = Zone.GetUtcOffset(local);
            if (offsetMinutes is not null && (int)offset.TotalMinutes != offsetMinutes)
                throw new InvalidLocalTimeException(
                    $"The UTC offset {offsetMinutes} does not match Europe/Zurich at {local:yyyy-MM-dd HH:mm} (expected {(int)offset.TotalMinutes}).");
        }
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The Europe/Zurich date of a UTC instant (rule-based conversion, not a clock read).</summary>
    public static DateOnly LocalDateOf(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);
}

/// <summary>An admin-entered local time is nonexistent, ambiguous or inconsistent.</summary>
public sealed class InvalidLocalTimeException(string message) : Exception(message);
