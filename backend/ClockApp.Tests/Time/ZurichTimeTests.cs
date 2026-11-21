using ClockApp.Attendance.Time;

namespace ClockApp.Tests.Time;

public class ZurichTimeTests
{
    private static AttendanceTime Convert(string local, bool dst) =>
        ZurichTime.FromLocal(DateTime.Parse(local), dst, "test");

    [Fact]
    public void Winter_time_is_UTC_plus_one()
    {
        var t = Convert("2026-11-20T18:20:57", dst: false);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 17, 20, 57, TimeSpan.Zero), t.UtcInstant);
        Assert.Equal(TimeSpan.FromHours(1), t.UtcOffset);
        Assert.Equal(new DateOnly(2026, 11, 20), t.LocalDate);
    }

    [Fact]
    public void Summer_time_is_UTC_plus_two()
    {
        var t = Convert("2026-07-04T09:00:00", dst: true);

        Assert.Equal(new DateTimeOffset(2026, 7, 4, 7, 0, 0, TimeSpan.Zero), t.UtcInstant);
        Assert.Equal(TimeSpan.FromHours(2), t.UtcOffset);
    }

    [Fact]
    public void Fall_back_ambiguous_hour_uses_the_dst_flag_to_pick_the_occurrence()
    {
        // 2026-10-25 02:30 happens twice in Zurich: first in CEST (+2), then in CET (+1).
        var first = Convert("2026-10-25T02:30:00", dst: true);
        var second = Convert("2026-10-25T02:30:00", dst: false);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), first.UtcInstant);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), second.UtcInstant);
        Assert.Equal(TimeSpan.FromHours(1), second.UtcInstant - first.UtcInstant);
    }

    [Fact]
    public void Spring_forward_gap_is_rejected()
    {
        // 2026-03-29 02:30 does not exist in Zurich.
        Assert.Throws<TimeSourceException>(() => Convert("2026-03-29T02:30:00", dst: true));
    }

    [Theory]
    [InlineData("2026-11-20T18:20:57", true)]   // winter date but DST reported
    [InlineData("2026-07-04T09:00:00", false)]  // summer date but no DST reported
    public void Dst_flag_contradicting_the_zone_rules_is_rejected(string local, bool dst)
    {
        Assert.Throws<TimeSourceException>(() => Convert(local, dst));
    }

    [Fact]
    public void Local_date_around_midnight_is_the_zurich_date_not_the_utc_date()
    {
        // 00:30 Zurich on 1 June is still 31 May in UTC; WorkDate must be the Zurich date.
        var t = Convert("2026-06-01T00:30:00", dst: true);

        Assert.Equal(new DateOnly(2026, 6, 1), t.LocalDate);
        Assert.Equal(new DateOnly(2026, 5, 31), DateOnly.FromDateTime(t.UtcInstant.UtcDateTime));
    }
}

public class ZurichLocalInputTests
{
    [Fact]
    public void Ordinary_winter_and_summer_times_convert_with_the_zone_offset()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T07:00:00Z"), ZurichTime.LocalToUtc(new DateTime(2026, 11, 20, 8, 0, 0)));
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T06:00:00Z"), ZurichTime.LocalToUtc(new DateTime(2026, 7, 1, 8, 0, 0)));
    }

    [Fact]
    public void Nonexistent_spring_forward_time_is_rejected()
    {
        Assert.Throws<InvalidLocalTimeException>(() => ZurichTime.LocalToUtc(new DateTime(2026, 3, 29, 2, 30, 0)));
    }

    [Fact]
    public void Ambiguous_fall_back_time_needs_an_offset_and_each_offset_gives_a_different_instant()
    {
        var local = new DateTime(2026, 10, 25, 2, 30, 0);

        Assert.Throws<InvalidLocalTimeException>(() => ZurichTime.LocalToUtc(local));
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T00:30:00Z"), ZurichTime.LocalToUtc(local, 120)); // first, CEST
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T01:30:00Z"), ZurichTime.LocalToUtc(local, 60));  // second, CET
        Assert.Throws<InvalidLocalTimeException>(() => ZurichTime.LocalToUtc(local, 90));
    }

    [Fact]
    public void An_offset_that_contradicts_the_zone_rules_is_rejected()
    {
        Assert.Throws<InvalidLocalTimeException>(() => ZurichTime.LocalToUtc(new DateTime(2026, 7, 1, 8, 0, 0), 60));
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T06:00:00Z"), ZurichTime.LocalToUtc(new DateTime(2026, 7, 1, 8, 0, 0), 120));
    }

    [Fact]
    public void Local_time_of_an_instant_is_zurich_wall_clock_including_across_midnight()
    {
        Assert.Equal(new DateTime(2031, 7, 1, 0, 30, 0), ZurichTime.ToLocal(DateTimeOffset.Parse("2031-06-30T22:30:00Z"))); // CEST
        Assert.Equal(new DateTime(2031, 12, 31, 23, 30, 0), ZurichTime.ToLocal(DateTimeOffset.Parse("2031-12-31T22:30:00Z"))); // CET
    }

    [Fact]
    public void Local_date_of_an_instant_uses_zurich_not_utc()
    {
        Assert.Equal(new DateOnly(2026, 7, 1), ZurichTime.LocalDateOf(DateTimeOffset.Parse("2026-06-30T22:30:00Z")));
        Assert.Equal(new DateOnly(2026, 12, 31), ZurichTime.LocalDateOf(DateTimeOffset.Parse("2026-12-31T22:30:00Z")));
    }
}

public class ZurichFromUtcTests
{
    private static AttendanceTime Utc(string utc, bool dst, int offsetMinutes) =>
        ZurichTime.FromUtc(DateTimeOffset.Parse(utc), dst, TimeSpan.FromMinutes(offsetMinutes), "test");

    [Fact]
    public void Winter_and_summer_instants_get_the_right_local_time()
    {
        Assert.Equal(new DateTime(2026, 11, 21, 13, 33, 29), Utc("2026-11-21T12:33:29Z", false, 60).LocalDateTime);
        Assert.Equal(new DateTime(2026, 7, 4, 9, 0, 0), Utc("2026-07-04T07:00:00Z", true, 120).LocalDateTime);
    }

    [Fact]
    public void Both_occurrences_of_the_fall_back_hour_are_distinct_instants_with_the_right_offsets()
    {
        var first = Utc("2026-10-25T00:30:00Z", true, 120);   // 02:30 CEST
        var second = Utc("2026-10-25T01:30:00Z", false, 60);  // 02:30 CET

        Assert.Equal(first.LocalDateTime, second.LocalDateTime);
        Assert.NotEqual(first.UtcInstant, second.UtcInstant);
    }

    [Fact]
    public void Spring_forward_instant_maps_past_the_gap()
    {
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0), Utc("2026-03-29T01:00:00Z", true, 120).LocalDateTime);
    }

    [Fact]
    public void Dst_flag_or_offset_contradicting_the_rules_is_rejected()
    {
        Assert.Throws<TimeSourceException>(() => Utc("2026-07-04T07:00:00Z", false, 120));
        Assert.Throws<TimeSourceException>(() => Utc("2026-07-04T07:00:00Z", true, 60));
    }
}
