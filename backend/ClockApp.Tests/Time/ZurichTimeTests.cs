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
