using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using ClockApp.Tests.Data;
using Microsoft.AspNetCore.Identity;

namespace ClockApp.Tests.Attendance;

/// <summary>Pure calculation tests: no database, effective records are built directly.</summary>
public class MonthlyReportCalculationTests
{
    private const int Default = 480; // 8 h

    private static EffectiveRecord Rec(string inLocal, string? outLocal, DateOnly? workDate = null, bool manual = false)
    {
        var i = ZurichTime.LocalToUtc(DateTime.Parse(inLocal), OffsetFor(inLocal));
        DateTimeOffset? o = outLocal is null ? null : ZurichTime.LocalToUtc(DateTime.Parse(outLocal), OffsetFor(outLocal));
        return new EffectiveRecord(1, null, i, o, workDate ?? ZurichTime.LocalDateOf(i), manual, false, o is null, i, o);
    }

    // Test data avoids the ambiguous hour, so the offset can be left to the zone rules.
    private static int? OffsetFor(string _) => null;

    private static MonthlyReportResult Build(int y, int m, params EffectiveRecord[] r) => MonthlyReport.Build(r, Default, y, m);

    [Fact]
    public void Exact_above_and_below_the_default_duration()
    {
        var r = Build(2026, 2,
            Rec("2026-02-02T08:00:00", "2026-02-02T16:00:00"),   // exactly 8 h
            Rec("2026-02-03T08:00:00", "2026-02-03T17:30:00"),   // 9.5 h  -> +90
            Rec("2026-02-04T08:00:00", "2026-02-04T14:00:00"));  // 6 h    -> -120

        Assert.Equal([0L, 90L, -120L], r.Days.Select(d => d.DifferenceMinutes!.Value));
        Assert.Equal(480 + 570 + 360, r.Totals.WorkedMinutes);
        Assert.Equal(3 * 480, r.Totals.ExpectedMinutes);
        Assert.Equal(-30, r.Totals.DifferenceMinutes);
    }

    [Fact]
    public void Several_records_on_one_day_are_summed()
    {
        var r = Build(2026, 2,
            Rec("2026-02-02T08:00:00", "2026-02-02T12:00:00"),
            Rec("2026-02-02T13:00:00", "2026-02-02T17:00:00"));

        var day = Assert.Single(r.Days);
        Assert.Equal(480, day.WorkedMinutes);
        Assert.Equal(0, day.DifferenceMinutes);
        Assert.Equal(2, day.Records.Count);
        Assert.Equal(1, r.Totals.DaysWorked);
    }

    [Fact]
    public void Open_records_are_listed_but_excluded_from_totals()
    {
        var r = Build(2026, 2,
            Rec("2026-02-02T08:00:00", "2026-02-02T16:00:00"),
            Rec("2026-02-03T08:00:00", null));

        Assert.True(r.Days[1].HasOpenRecord);
        Assert.Null(r.Days[1].DifferenceMinutes);
        Assert.Equal(480, r.Totals.WorkedMinutes);
        Assert.Equal(480, r.Totals.ExpectedMinutes); // the open day is not expected-counted
        Assert.Equal(1, r.Totals.DaysWorked);
    }

    [Fact]
    public void Manual_entries_are_flagged()
    {
        var r = Build(2026, 2, Rec("2026-02-02T08:00:00", "2026-02-02T16:00:00", manual: true));

        Assert.True(r.Days[0].Records[0].IsManual);
    }

    [Fact]
    public void Spring_forward_day_counts_real_elapsed_time()
    {
        // 2026-03-29: 01:00 -> 05:00 local is only 3 real hours (02:00-03:00 does not exist).
        var r = Build(2026, 3, Rec("2026-03-29T01:00:00", "2026-03-29T05:00:00"));

        Assert.Equal(180, r.Days[0].WorkedMinutes);
    }

    [Fact]
    public void Fall_back_day_counts_real_elapsed_time()
    {
        // 2026-10-25: 01:00 -> 05:00 local is 5 real hours (02:00-03:00 happens twice).
        var r = Build(2026, 10, Rec("2026-10-25T01:00:00", "2026-10-25T05:00:00"));

        Assert.Equal(300, r.Days[0].WorkedMinutes);
    }

    [Fact]
    public void A_night_shift_counts_on_the_day_it_started_and_in_the_month_it_started()
    {
        var night = Rec("2026-06-30T22:00:00", "2026-07-01T06:00:00"); // WorkDate = 30 June

        var june = Build(2026, 6, night);
        var july = Build(2026, 7, night);

        Assert.Equal(480, june.Totals.WorkedMinutes);
        Assert.Single(june.Days);
        Assert.Empty(july.Days);
    }

    [Fact]
    public void The_month_boundary_follows_the_zurich_date_not_utc()
    {
        // 00:30 Zurich on 1 July is still 30 June in UTC; it belongs to July.
        var r = Rec("2026-07-01T00:30:00", "2026-07-01T08:30:00");

        Assert.Equal(new DateOnly(2026, 7, 1), r.WorkDate);
        Assert.Single(Build(2026, 7, r).Days);
        Assert.Empty(Build(2026, 6, r).Days);
    }

    [Fact]
    public void An_empty_month_has_zero_totals()
    {
        var r = Build(2026, 2);

        Assert.Empty(r.Days);
        Assert.Equal(0, r.Totals.WorkedMinutes);
        Assert.Equal(0, r.Totals.DifferenceMinutes);
    }
}

/// <summary>Endpoint tests: own-data isolation, corrections applied, validation.</summary>
public sealed class MonthlyReportEndpointTests : IClassFixture<DatabaseFixture>
{
    private const string Password = "test-password-123";
    private readonly DatabaseFixture _db;

    public MonthlyReportEndpointTests(DatabaseFixture db) => _db = db;

    private async Task<(User user, string token)> NewEmployeeAsync(HttpClient client)
    {
        await using var db = _db.CreateContext();
        var user = new User { Email = $"rep-{Guid.NewGuid():N}@test.local", FullName = "Reporter", Role = UserRole.Employee };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        return (user, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!);
    }

    private static Task<HttpResponseMessage> Get(HttpClient c, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c.SendAsync(req);
    }

    [RequiresDbFact]
    public async Task Report_contains_only_the_callers_own_records_and_applies_corrections()
    {
        await using var factory = new ApiFactory(_db.ConnectionString);
        var client = factory.CreateClient();
        var (alice, aliceToken) = await NewEmployeeAsync(client);
        var (bob, bobToken) = await NewEmployeeAsync(client);

        await using (var db = _db.CreateContext())
        {
            // Alice: a punch (8 h) corrected to 9 h; Bob: one punch that Alice must never see.
            var punch = new AttendanceRecord { UserId = alice.Id, ClockInUtc = DateTimeOffset.Parse("2026-02-02T07:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-02T15:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "stub" };
            db.AttendanceRecords.Add(punch);
            db.AttendanceRecords.Add(new AttendanceRecord { UserId = bob.Id, ClockInUtc = DateTimeOffset.Parse("2026-02-02T07:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-02T11:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "stub" });
            await db.SaveChangesAsync();
            db.AttendanceCorrections.Add(new AttendanceCorrection
            {
                Kind = CorrectionKind.Adjust, AttendanceRecordId = punch.Id, UserId = alice.Id, WorkDate = new DateOnly(2026, 2, 2),
                CorrectedClockInUtc = punch.ClockInUtc, CorrectedClockOutUtc = DateTimeOffset.Parse("2026-02-02T16:00:00Z"),
                PreviousClockInUtc = punch.ClockInUtc, PreviousClockOutUtc = punch.ClockOutUtc,
                Reason = "Stayed an hour longer", CreatedByUserId = bob.Id, CreatedAtUtc = DateTimeOffset.Parse("2026-02-03T08:00:00Z"),
            });
            await db.SaveChangesAsync();
        }

        var alicesReport = await (await Get(client, aliceToken, "/api/attendance/me/month?year=2026&month=2")).Content.ReadFromJsonAsync<JsonElement>();
        var bobsReport = await (await Get(client, bobToken, "/api/attendance/me/month?year=2026&month=2")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(540, alicesReport.GetProperty("totals").GetProperty("workedMinutes").GetInt64()); // corrected 9 h
        Assert.Equal(60, alicesReport.GetProperty("days")[0].GetProperty("differenceMinutes").GetInt64());
        Assert.True(alicesReport.GetProperty("days")[0].GetProperty("records")[0].GetProperty("isAdjusted").GetBoolean());
        Assert.Equal(240, bobsReport.GetProperty("totals").GetProperty("workedMinutes").GetInt64()); // only his own 4 h
    }

    [RequiresDbFact]
    public async Task Invalid_month_is_400_and_anonymous_is_401()
    {
        await using var factory = new ApiFactory(_db.ConnectionString);
        var client = factory.CreateClient();
        var (_, token) = await NewEmployeeAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await Get(client, token, "/api/attendance/me/month?year=2026&month=13")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/attendance/me/month")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, token, "/api/attendance/me/month")).StatusCode); // defaults to current month
    }
}
