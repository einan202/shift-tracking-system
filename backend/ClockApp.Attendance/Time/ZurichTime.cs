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
    /// For providers that report the UTC instant directly: no ambiguity to resolve. TimeZoneInfo is used only
    /// to CHECK the provider's DST flag and offset against the Europe/Zurich rules, never as a clock.
    /// </summary>
    /// <exception cref="TimeSourceException">The reported DST flag or offset contradicts the zone rules.</exception>
    public static AttendanceTime FromUtc(DateTimeOffset utc, bool dstActive, TimeSpan reportedOffset, string provider)
    {
        utc = utc.ToUniversalTime();
        var offset = Zone.GetUtcOffset(utc);
        if (Zone.IsDaylightSavingTime(utc) != dstActive)
            throw new TimeSourceException($"Time source reported dst={dstActive} for {utc:O}, which contradicts {ZoneId} rules.");
        if (offset != reportedOffset)
            throw new TimeSourceException($"Time source reported offset {reportedOffset} for {utc:O}, expected {offset} in {ZoneId}.");

        var local = DateTime.SpecifyKind(utc.ToOffset(offset).DateTime, DateTimeKind.Unspecified);
        return new AttendanceTime(utc, local, offset, dstActive, provider);
    }

    /// <summary>
    /// Converts an admin-typed Zurich wall-clock time to a UTC instant (for corrections, never for punches).
    /// Nonexistent times (spring-forward gap) are rejected; ambiguous times (fall-back hour) need the
    /// UTC offset in minutes (+60 or +120) to say which occurrence is meant.
    /// </summary>
    /// <exception cref="InvalidLocalTimeException"/>
    public static DateTimeOffset LocalToUtc(DateTime local, int? offsetMinutes = null)
    {
        // Same year window as the reports (2000-2100); also keeps the UTC conversion away from DateTime's min/max range.
        if (local.Year is < 2000 or > 2100)
            throw new InvalidLocalTimeException($"The year must be between 2000 and 2100 (got {local.Year}).");
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

    /// <summary>The Europe/Zurich wall-clock time of a UTC instant (rule-based conversion, not a clock read).</summary>
    public static DateTime ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone).DateTime;

    /// <summary>The Europe/Zurich date of a UTC instant (rule-based conversion, not a clock read).</summary>
    public static DateOnly LocalDateOf(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);
}

/// <summary>An admin-entered local time is nonexistent, ambiguous or inconsistent.</summary>
public sealed class InvalidLocalTimeException(string message) : Exception(message);
