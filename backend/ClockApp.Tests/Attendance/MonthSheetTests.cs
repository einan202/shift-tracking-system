using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using System.Net.Http.Json;
using ClockApp.Attendance.Time;

namespace ClockApp.Tests.Attendance;

public class MonthSheetTests
{
    // A schedule: Mon-Thu 8h30, Fri 6h, weekend off. 2026-06-01 is a Monday.
    private static readonly EmployeeScheduleVersion Week = new()
    {
        Id = 1, EffectiveFrom = new DateOnly(2026, 6, 1), ExpectedMinutesMon = 510, ExpectedMinutesTue = 510,
        ExpectedMinutesWed = 510, ExpectedMinutesThu = 510, ExpectedMinutesFri = 360,
    };
    private static readonly DateOnly Start = new(2026, 6, 1);

    private static DateTimeOffset Z(string local) => ZurichTime.LocalToUtc(DateTime.Parse(local), null);

    private static EffectiveRecord Punch(string inLocal, string? outLocal, bool open = false) =>
        new(1, null, Z(inLocal), outLocal is null ? null : Z(outLocal), ZurichTime.LocalDateOf(Z(inLocal)), false, false, outLocal is null || open,
            Z(inLocal), outLocal is null ? null : Z(outLocal), "stub");

    private static EffectiveRecord Corrected(string inLocal, string outLocal, string originalOut) =>
        new(1, null, Z(inLocal), Z(outLocal), ZurichTime.LocalDateOf(Z(inLocal)), false, true, false, Z(inLocal), Z(originalOut), "stub");

    private static SheetDay Day(MonthSheetResult r, int d) => r.Days.Single(x => x.Date.Day == d);

    private static MonthSheetResult Build(DateOnly today, params EffectiveRecord[] rec) =>
        MonthSheet.Build(2026, 6, rec, [Week], Start, today);

    [Fact]
    public void Every_weekday_uses_its_own_independent_duration_for_expected_balance_and_totals()
    {
        // Mon 8h30, Tue 9h, Wed 7h45, Thu 8h, Fri 6h, weekend off.
        var own = new EmployeeScheduleVersion
        {
            Id = 2, EffectiveFrom = Start, ExpectedMinutesMon = 510, ExpectedMinutesTue = 540, ExpectedMinutesWed = 465,
            ExpectedMinutesThu = 480, ExpectedMinutesFri = 360,
        };
        var r = MonthSheet.Build(2026, 6, [Punch("2026-06-01T09:00", "2026-06-01T17:00"), Punch("2026-06-02T09:00", "2026-06-02T18:30")],
            [own], Start, new DateOnly(2026, 7, 1));

        Assert.Equal(510, Day(r, 1).ExpectedMinutes);   // Mon
        Assert.Equal(540, Day(r, 2).ExpectedMinutes);   // Tue
        Assert.Equal(465, Day(r, 3).ExpectedMinutes);   // Wed
        Assert.Equal(480, Day(r, 4).ExpectedMinutes);   // Thu
        Assert.Equal(360, Day(r, 5).ExpectedMinutes);   // Fri
        Assert.Equal(0, Day(r, 6).ExpectedMinutes);     // Sat
        Assert.Equal(0, Day(r, 7).ExpectedMinutes);     // Sun
        Assert.Equal(-30, Day(r, 1).BalanceMinutes);    // worked 8h against 8h30
        Assert.Equal(+30, Day(r, 2).BalanceMinutes);    // worked 9h30 against 9h
        Assert.Equal(5 * 510 + 5 * 540 + 4 * 465 + 4 * 480 + 4 * 360, r.MonthToDate.WholeMonthExpectedMinutes);
    }

    [Fact]
    public void Lists_every_calendar_day_with_expected_per_weekday()
    {
        var r = Build(new DateOnly(2026, 7, 1));   // month is over

        Assert.Equal(30, r.Days.Count);
        Assert.Equal(510, Day(r, 1).ExpectedMinutes);   // Mon
        Assert.Equal(360, Day(r, 5).ExpectedMinutes);   // Fri
        Assert.Equal(0, Day(r, 6).ExpectedMinutes);     // Sat
        // June 2026 has 5 Mondays, 5 Tuesdays, 4 Wednesdays, 4 Thursdays (8h30 each) and 4 Fridays (6h).
        Assert.Equal(18 * 510 + 4 * 360, r.MonthToDate.WholeMonthExpectedMinutes);
    }

    [Fact]
    public void Shift_is_first_clock_in_to_last_clock_out_and_breaks_count_as_worked()
    {
        var r = Build(new DateOnly(2026, 6, 20),
            Punch("2026-06-02T08:00", "2026-06-02T12:00"), Punch("2026-06-02T13:00", "2026-06-02T17:00"));

        var d = Day(r, 2);
        Assert.Equal(Z("2026-06-02T08:00"), d.ClockIn);
        Assert.Equal(Z("2026-06-02T17:00"), d.ClockOut);
        Assert.Equal(9 * 60, d.WorkedMinutes);                 // the one-hour break counts
        Assert.Equal(9 * 60 - 510, d.BalanceMinutes);          // +30 min
    }

    [Fact]
    public void A_past_expected_day_without_a_shift_is_a_deficit_and_a_non_working_day_is_empty()
    {
        var r = Build(new DateOnly(2026, 6, 20));

        Assert.Equal(-510, Day(r, 3).BalanceMinutes);          // Wednesday, nothing recorded
        Assert.Null(Day(r, 6).BalanceMinutes);                 // Saturday
        Assert.Null(Day(r, 3).WorkedMinutes);
    }

    [Fact]
    public void Future_days_show_expected_only_and_stay_out_of_month_to_date()
    {
        var r = Build(new DateOnly(2026, 6, 10));

        var future = Day(r, 11);
        Assert.True(future.IsFuture);
        Assert.Equal(510, future.ExpectedMinutes);
        Assert.Null(future.WorkedMinutes); Assert.Null(future.BalanceMinutes);
        Assert.True(r.MonthToDate.WholeMonthExpectedMinutes > r.MonthToDate.ExpectedMinutes);
    }

    [Fact]
    public void Today_before_clock_out_shows_expected_only_and_is_not_in_the_totals_until_finished()
    {
        var running = Build(new DateOnly(2026, 6, 4), Punch("2026-06-04T08:00", null));
        var today = Day(running, 4);
        Assert.True(today.IsToday); Assert.True(today.InProgress);
        Assert.Equal(510, today.ExpectedMinutes);
        Assert.Null(today.WorkedMinutes); Assert.Null(today.BalanceMinutes);
        Assert.Equal(0, running.MonthToDate.WorkedMinutes);

        var finished = Build(new DateOnly(2026, 6, 4), Punch("2026-06-04T08:00", "2026-06-04T16:30"));
        Assert.Equal(510, Day(finished, 4).WorkedMinutes);
        Assert.Equal(0, Day(finished, 4).BalanceMinutes);
    }

    [Fact]
    public void An_open_interval_on_a_past_day_is_in_progress_and_counts_nothing()
    {
        var r = Build(new DateOnly(2026, 6, 20), Punch("2026-06-02T08:00", null));

        var d = Day(r, 2);
        Assert.True(d.InProgress);
        Assert.Null(d.WorkedMinutes); Assert.Null(d.BalanceMinutes); Assert.Null(d.ClockOut);
        Assert.Equal(0, r.MonthToDate.WorkedMinutes);
    }

    [Fact]
    public void A_later_date_clock_out_needs_correction_and_the_multi_day_span_is_never_counted()
    {
        // Monday 09:00 forgotten, clock-out Thursday 08:00 (71 h).
        var r = Build(new DateOnly(2026, 6, 20), Punch("2026-06-01T09:00", "2026-06-04T08:00"));

        var d = Day(r, 1);
        Assert.True(d.NeedsCorrection);
        Assert.Equal(Z("2026-06-01T09:00"), d.ClockIn);
        Assert.Null(d.ClockOut); Assert.Null(d.WorkedMinutes); Assert.Null(d.BalanceMinutes);
        Assert.Equal(0, r.MonthToDate.WorkedMinutes);
        // An unresolved day is excluded from the totals: month-to-date Expected is the no-records baseline minus that Monday.
        Assert.Equal(Build(new DateOnly(2026, 6, 20)).MonthToDate.ExpectedMinutes - 510, r.MonthToDate.ExpectedMinutes);
    }

    [Fact]
    public void A_reasoned_correction_of_the_end_makes_the_day_count_and_flags_the_clock_out_as_manual()
    {
        var r = Build(new DateOnly(2026, 6, 20), Corrected("2026-06-01T09:00", "2026-06-01T17:30", originalOut: "2026-06-04T08:00"));

        var d = Day(r, 1);
        Assert.False(d.NeedsCorrection);
        Assert.Equal(510, d.WorkedMinutes);
        Assert.Equal(0, d.BalanceMinutes);
        Assert.True(d.ClockOutIsManual);
        Assert.False(d.ClockInIsManual);                          // only the clock-out was set manually
    }

    [Fact]
    public void Overnight_shifts_count_only_when_the_schedule_allows_them_and_never_beyond_24_hours()
    {
        var overnight = new EmployeeScheduleVersion { Id = 2, EffectiveFrom = Start, ExpectedMinutesMon = 480, AllowsOvernightShifts = true };
        var night = Punch("2026-06-01T22:00", "2026-06-02T06:00");
        var tooLong = Punch("2026-06-08T09:00", "2026-06-09T11:00");   // 26 h

        var allowed = MonthSheet.Build(2026, 6, [night, tooLong], [overnight], Start, new DateOnly(2026, 6, 20));
        var notAllowed = Build(new DateOnly(2026, 6, 20), night);

        Assert.Equal(480, Day(allowed, 1).WorkedMinutes);          // 22:00 -> 06:00 on the start date
        Assert.True(Day(allowed, 8).NeedsCorrection);              // beyond 24 h still needs correction
        Assert.True(Day(notAllowed, 1).NeedsCorrection);           // default employee: later date needs correction
    }

    [Fact]
    public void Voided_intervals_do_not_count_and_dates_before_employment_start_expect_nothing()
    {
        var voided = Punch("2026-06-02T08:00", "2026-06-02T17:00") with { IsVoided = true };
        var r = MonthSheet.Build(2026, 6, [voided], [Week], new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20));

        Assert.Null(Day(r, 2).ClockIn);                            // deleted shift: empty row
        Assert.Equal(0, Day(r, 2).ExpectedMinutes);                // before the start date
        Assert.Null(Day(r, 2).BalanceMinutes);
        Assert.Equal(510, Day(r, 16).ExpectedMinutes);             // after the start date
    }

    [Fact]
    public void A_schedule_change_applies_only_from_its_date_so_earlier_days_are_unchanged()
    {
        var v2 = new EmployeeScheduleVersion { Id = 2, EffectiveFrom = new DateOnly(2026, 6, 15), ExpectedMinutesMon = 240, ExpectedMinutesFri = 240 };
        var r = MonthSheet.Build(2026, 6, [], [Week, v2], Start, new DateOnly(2026, 7, 1));

        Assert.Equal(510, Day(r, 8).ExpectedMinutes);              // Monday before the change
        Assert.Equal(240, Day(r, 15).ExpectedMinutes);             // Monday on the effective date
        Assert.Equal(0, Day(r, 16).ExpectedMinutes);               // Tuesday not in the new version
    }

    [Fact]
    public void Dst_days_use_the_real_elapsed_time()
    {
        // 2026-03-29 (spring forward): 01:00 -> 05:00 local is only 3 real hours.
        var punch = Punch("2026-03-29T01:00", "2026-03-29T05:00");
        var v = new EmployeeScheduleVersion { Id = 1, EffectiveFrom = new DateOnly(2026, 3, 1), ExpectedMinutesSun = 180 };

        var r = MonthSheet.Build(2026, 3, [punch], [v], new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 5));

        Assert.Equal(180, r.Days.Single(x => x.Date.Day == 29).WorkedMinutes);
    }

    [Fact]
    public void Fall_back_day_counts_the_extra_real_hour()
    {
        // 2026-10-25 (clocks go back 03:00 -> 02:00): 01:00 -> 05:00 local is 5 real hours, not 4.
        var punch = Punch("2026-10-25T01:00", "2026-10-25T05:00");
        var v = new EmployeeScheduleVersion { Id = 1, EffectiveFrom = new DateOnly(2026, 10, 1), ExpectedMinutesSun = 300 };

        var r = MonthSheet.Build(2026, 10, [punch], [v], new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 2));

        Assert.Equal(300, r.Days.Single(x => x.Date.Day == 25).WorkedMinutes);
        Assert.Equal(0, r.Days.Single(x => x.Date.Day == 25).BalanceMinutes);
    }

    [Fact]
    public void Clock_out_after_midnight_needs_correction_unless_overnight_shifts_are_allowed()
    {
        var punch = Punch("2026-06-02T22:00", "2026-06-03T02:00");   // Tuesday 22:00 -> Wednesday 02:00

        var normal = Build(new DateOnly(2026, 7, 1), punch);
        Assert.True(Day(normal, 2).NeedsCorrection);       // nothing counts until a reasoned correction sets the end
        Assert.Null(Day(normal, 2).WorkedMinutes);
        Assert.Null(Day(normal, 3).WorkedMinutes);

        var night = new EmployeeScheduleVersion
        {
            Id = 3, EffectiveFrom = Start, AllowsOvernightShifts = true, ExpectedMinutesMon = 510, ExpectedMinutesTue = 510, ExpectedMinutesWed = 510,
        };
        var overnight = MonthSheet.Build(2026, 6, [punch], [night], Start, new DateOnly(2026, 7, 1));
        Assert.False(Day(overnight, 2).NeedsCorrection);
        Assert.Equal(240, Day(overnight, 2).WorkedMinutes); // belongs to the start date
        Assert.Null(Day(overnight, 3).WorkedMinutes);
    }
}

/// <summary>The employee timesheet endpoint: one consolidated shift per day, no history for employees.</summary>
public sealed class TimesheetEndpointTests : IClassFixture<ClockApp.Tests.Data.DatabaseFixture>
{
    private readonly ClockApp.Tests.Data.DatabaseFixture _db;
    public TimesheetEndpointTests(ClockApp.Tests.Data.DatabaseFixture db) => _db = db;

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Returns_every_day_with_consolidated_shifts_manual_flags_and_no_history()
    {
        var factory = new ApiFactory(_db.AppConnectionString, clock: new FakeClock(DateTimeOffset.UtcNow));
        await using var _ = factory;
        var client = factory.CreateClient();
        int userId;
        string email;
        await using (var db = _db.CreateContext())
        {
            var admin = new User { Email = $"ta-{Guid.NewGuid():N}@t.local", FullName = "A", Role = UserRole.Admin };
            var emp = new User { Email = $"te-{Guid.NewGuid():N}@t.local", FullName = "E", Role = UserRole.Employee, EmploymentStartDate = new DateOnly(2026, 2, 1) };
            emp.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(emp, "test-password-123");
            db.Users.AddRange(admin, emp);
            await db.SaveChangesAsync();
            userId = emp.Id; email = emp.Email;
            db.EmployeeScheduleVersions.Add(new EmployeeScheduleVersion { UserId = userId, EffectiveFrom = new DateOnly(2026, 2, 1), ExpectedMinutesMon = 510, ExpectedMinutesTue = 510, CreatedByUserId = admin.Id, CreatedAtUtc = DateTimeOffset.UtcNow });
            // Monday 2026-02-02: two punches (breaks count); Tuesday 2026-02-03: punch corrected by an admin (clock-out manual).
            db.AttendanceRecords.Add(new AttendanceRecord { UserId = userId, ClockInUtc = DateTimeOffset.Parse("2026-02-02T07:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-02T11:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "stub" });
            db.AttendanceRecords.Add(new AttendanceRecord { UserId = userId, ClockInUtc = DateTimeOffset.Parse("2026-02-02T12:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-02T16:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "stub" });
            var tue = new AttendanceRecord { UserId = userId, ClockInUtc = DateTimeOffset.Parse("2026-02-03T07:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-03T15:00:00Z"), WorkDate = new DateOnly(2026, 2, 3), TimeProvider = "stub" };
            db.AttendanceRecords.Add(tue);
            await db.SaveChangesAsync();
            db.AttendanceCorrections.Add(new AttendanceCorrection
            {
                Kind = CorrectionKind.Adjust, AttendanceRecordId = tue.Id, UserId = userId, WorkDate = new DateOnly(2026, 2, 3),
                CorrectedClockInUtc = tue.ClockInUtc, CorrectedClockOutUtc = DateTimeOffset.Parse("2026-02-03T16:30:00Z"),
                PreviousClockInUtc = tue.ClockInUtc, PreviousClockOutUtc = tue.ClockOutUtc,
                Reason = "secret reason text", ReasonCode = ReasonCodes.WrongTimePunched, CreatedByUserId = admin.Id, CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "test-password-123" });
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/attendance/me/timesheet?year=2026&month=2");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var json = System.Text.Json.JsonDocument.Parse(raw).RootElement;

        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var days = json.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(28, days.Count);                                                   // every calendar day of February 2026
        var mon = days[1]; var tuesday = days[2];
        Assert.Equal(9 * 60, mon.GetProperty("workedMinutes").GetInt32());              // 08:00 -> 17:00 Zurich, 1 h break counted
        Assert.Equal(510, mon.GetProperty("expectedMinutes").GetInt32());
        Assert.False(mon.GetProperty("clockInIsManual").GetBoolean());
        Assert.True(tuesday.GetProperty("clockOutIsManual").GetBoolean());              // only the clock-out was set manually
        Assert.False(tuesday.GetProperty("clockInIsManual").GetBoolean());
        Assert.Equal(570, tuesday.GetProperty("workedMinutes").GetInt32());             // corrected 08:00 -> 17:30
        // Employees never see reasons, edit times, history or punches.
        Assert.DoesNotContain("secret reason text", raw);
        Assert.DoesNotContain("reason", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("history", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", raw, StringComparison.OrdinalIgnoreCase);
    }
}
