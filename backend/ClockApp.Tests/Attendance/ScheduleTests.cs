using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Tests.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

/// <summary>Pure schedule resolution: effective-dated, append-only versions.</summary>
public class ScheduleResolverTests
{
    private static EmployeeScheduleVersion V(long id, string from, short mon, short fri = 0, short sun = 0) => new()
    {
        Id = id, EffectiveFrom = DateOnly.Parse(from), ExpectedMinutesMon = mon, ExpectedMinutesFri = fri, ExpectedMinutesSun = sun,
    };

    // 2026-06-01 is a Monday, 2026-06-05 a Friday, 2026-07-03 a Friday, 2026-07-06 a Monday.
    [Fact]
    public void A_change_applies_only_from_its_effective_date_and_never_to_earlier_dates()
    {
        var versions = new[] { V(1, "2026-06-01", 510, fri: 360), V(2, "2026-07-01", 510, fri: 240) };

        Assert.Equal(360, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 1)));   // June Friday: old
        Assert.Equal(240, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 7, 3), new DateOnly(2026, 6, 1)));   // July Friday: new
        Assert.Equal(0, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 1)));     // Saturday: not a working day in either version
    }

    [Fact]
    public void The_day_before_an_effective_date_still_uses_the_old_version()
    {
        var versions = new[] { V(1, "2026-06-01", 100, 100), V(2, "2026-07-01", 200, 200) };

        Assert.Equal(100, ScheduleResolver.VersionFor(versions, new DateOnly(2026, 6, 30))!.ExpectedMinutesMon);
        Assert.Equal(200, ScheduleResolver.VersionFor(versions, new DateOnly(2026, 7, 1))!.ExpectedMinutesMon);
    }

    [Fact]
    public void Versions_with_the_same_date_resolve_to_the_newest()
    {
        var versions = new[] { V(5, "2026-07-01", 100), V(9, "2026-07-01", 300) };

        Assert.Equal(300, ScheduleResolver.VersionFor(versions, new DateOnly(2026, 7, 15))!.ExpectedMinutesMon);
    }

    [Fact]
    public void Dates_before_every_version_use_the_earliest_version_and_dates_before_employment_start_expect_nothing()
    {
        var versions = new[] { V(1, "2026-06-01", 510, 360) };

        Assert.Equal(510, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 5, 4), employmentStart: null));       // Monday before the first version
        Assert.Equal(0, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 20)));      // before the start date
        Assert.Equal(0, ScheduleResolver.ExpectedMinutes([], new DateOnly(2026, 6, 1), null));                                // no version at all
    }

    [Fact]
    public void Non_working_weekdays_expect_zero()
    {
        var versions = new[] { V(1, "2026-06-01", 510) };   // only Monday works

        Assert.Equal(510, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 6, 1), null));
        Assert.Equal(0, ScheduleResolver.ExpectedMinutes(versions, new DateOnly(2026, 6, 2), null));   // Tuesday
    }

    [Theory]
    [InlineData(new[] { 510, 510, 510, 510, 360, 0, 0 }, null)]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0, 1 }, null)]                       // a single working day
    [InlineData(new[] { 510, 0, 510, 510, 0, 0, 0 }, null)]                  // non-contiguous selection
    [InlineData(new[] { 0, 0, 0, 0, 0, 0, 0 }, "at least one")]
    [InlineData(new[] { 1441, 0, 0, 0, 0, 0, 0 }, "between 0 and 24")]
    [InlineData(new[] { -5, 100, 0, 0, 0, 0, 0 }, "between 0 and 24")]
    [InlineData(new[] { 100, 100 }, "all seven")]
    public void Validation(int[] minutes, string? expectedProblem)
    {
        var problem = ScheduleResolver.Validate(minutes);

        if (expectedProblem is null) Assert.Null(problem); else Assert.Contains(expectedProblem, problem);
    }
}

/// <summary>Admin API: employment start date, initial schedule, and effective-dated changes.</summary>
public sealed class ScheduleApiTests : IClassFixture<DatabaseFixture>
{
    private const string Password = "test-password-123";
    private readonly DatabaseFixture _db;

    public ScheduleApiTests(DatabaseFixture db) => _db = db;

    private async Task<(ApiFactory F, HttpClient C, string Admin, string Employee)> Start()
    {
        var f = new ApiFactory(_db.AppConnectionString, clock: new FakeClock(DateTimeOffset.UtcNow));
        var c = f.CreateClient();
        string Email(string r) => $"{r}-{Guid.NewGuid():N}@test.local";
        await using var db = _db.CreateContext();
        var admin = new User { Email = Email("sa"), FullName = "Sched Admin", Role = UserRole.Admin };
        var emp = new User { Email = Email("se"), FullName = "Sched Employee", Role = UserRole.Employee };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, Password);
        emp.PasswordHash = new PasswordHasher<User>().HashPassword(emp, Password);
        db.Users.AddRange(admin, emp);
        await db.SaveChangesAsync();
        async Task<string> Login(string email) =>
            (await (await c.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (f, c, await Login(admin.Email), await Login(emp.Email));
    }

    private static Task<HttpResponseMessage> Send(HttpClient c, string token, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return c.SendAsync(req);
    }

    private async Task<int> CreateEmployee(HttpClient c, string admin, object body)
    {
        var res = await Send(c, admin, HttpMethod.Post, "/api/admin/employees", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static string NewEmail() => $"new-{Guid.NewGuid():N}@test.local";

    [RequiresDbFact]
    public async Task A_new_employee_gets_a_start_date_and_the_default_schedule()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Default Schedule" });

        var json = await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(JsonValueKind.Null, json.GetProperty("employmentStartDate").ValueKind);
        var v = Assert.Single(json.GetProperty("versions").EnumerateArray());
        var minutes = v.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(0, minutes[5]); Assert.Equal(0, minutes[6]);                 // Saturday/Sunday off
        Assert.All(minutes.Take(5), m => Assert.Equal(minutes[0], m));            // Mon-Fri = company default
        Assert.False(v.GetProperty("allowsOvernightShifts").GetBoolean());
    }

    [RequiresDbFact]
    public async Task Company_default_week_is_per_weekday_and_prefills_new_employees()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var before = await (await Send(c, admin, HttpMethod.Get, "/api/admin/settings/shift")).Content.ReadFromJsonAsync<JsonElement>();
        try
        {
            int[] week = [510, 540, 465, 480, 360, 0, 0];
            var put = await Send(c, admin, HttpMethod.Put, "/api/admin/settings/shift", new { minutes = week });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var got = await (await Send(c, admin, HttpMethod.Get, "/api/admin/settings/shift")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(week, got.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray());

            // Invalid weeks are rejected; a new employee without an explicit schedule gets the company week.
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, admin, HttpMethod.Put, "/api/admin/settings/shift", new { minutes = new[] { 0, 0, 0, 0, 0, 0, 0 } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, admin, HttpMethod.Put, "/api/admin/settings/shift", new { minutes = new[] { 480, 480 } })).StatusCode);
            var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Prefilled" });
            var v = (await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versions").EnumerateArray().Single();
            Assert.Equal(week, v.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }
        finally
        {
            await Send(c, admin, HttpMethod.Put, "/api/admin/settings/shift", new { minutes = before.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray() });
        }
    }

    [RequiresDbFact]
    public async Task Five_different_weekday_durations_round_trip_and_editing_keeps_the_other_days()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        int[] week = [510, 540, 465, 480, 360, 0, 0];
        var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Own Durations", employmentStartDate = "2026-06-01", schedule = new { minutes = week } });

        async Task<int[][]> Versions() => (await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray();
        Assert.Equal(week, (await Versions()).Single());

        // Change only Wednesday (to 8h) from a later month: the new version keeps the other days as sent, the old one stays untouched.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        int[] edited = [510, 540, 480, 480, 360, 0, 0];
        var res = await Send(c, admin, HttpMethod.Post, $"/api/admin/employees/{id}/schedule", new { effectiveFrom = from.ToString("yyyy-MM-dd"), minutes = edited });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var all = await Versions();
        Assert.Equal(2, all.Length);
        Assert.Contains(week, all, new IntArrayComparer());
        Assert.Contains(edited, all, new IntArrayComparer());
    }

    private sealed class IntArrayComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? a, int[]? b) => a is not null && b is not null && a.SequenceEqual(b);
        public int GetHashCode(int[] o) => o.Aggregate(17, (h, x) => h * 31 + x);
    }

    [RequiresDbFact]
    public async Task A_custom_start_date_and_weekday_durations_are_stored()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var id = await CreateEmployee(c, admin, new
        {
            email = NewEmail(), fullName = "Custom", employmentStartDate = "2026-06-01",
            schedule = new { minutes = new[] { 510, 510, 510, 510, 240, 0, 0 }, allowsOvernightShifts = true },
        });

        var json = await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("2026-06-01", json.GetProperty("employmentStartDate").GetString());
        var v = Assert.Single(json.GetProperty("versions").EnumerateArray());
        Assert.Equal("2026-06-01", v.GetProperty("effectiveFrom").GetString());
        Assert.Equal([510, 510, 510, 510, 240, 0, 0], v.GetProperty("minutes").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.True(v.GetProperty("allowsOvernightShifts").GetBoolean());
    }

    [RequiresDbFact]
    public async Task Invalid_schedules_are_rejected_when_creating_and_nothing_is_written()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var email = NewEmail();

        foreach (var minutes in new[] { new[] { 0, 0, 0, 0, 0, 0, 0 }, new[] { 1441, 0, 0, 0, 0, 0, 0 }, new[] { 100, 100 } })
        {
            var res = await Send(c, admin, HttpMethod.Post, "/api/admin/employees", new { email, fullName = "Bad", schedule = new { minutes } });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        await using var db = _db.CreateContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }

    [RequiresDbFact]
    public async Task A_schedule_change_appends_a_version_and_keeps_history()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Change" });
        var today = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);                  // next month: always allowed
        var effective = new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd");

        var res = await Send(c, admin, HttpMethod.Post, $"/api/admin/employees/{id}/schedule", new { effectiveFrom = effective, minutes = new[] { 480, 480, 480, 480, 240, 0, 0 } });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>();
        var versions = json.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);                                                   // the original is kept, nothing updated
        Assert.Equal(effective, versions[1].GetProperty("effectiveFrom").GetString());
        Assert.Equal("Sched Admin", versions[1].GetProperty("createdBy").GetString());
    }

    [RequiresDbFact]
    public async Task A_change_effective_in_a_past_month_is_rejected()
    {
        var (f, c, admin, _) = await Start(); await using var _f = f;
        var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Retro" });
        var lastMonth = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

        var res = await Send(c, admin, HttpMethod.Post, $"/api/admin/employees/{id}/schedule",
            new { effectiveFrom = new DateOnly(lastMonth.Year, lastMonth.Month, 1).ToString("yyyy-MM-dd"), minutes = new[] { 480, 0, 0, 0, 0, 0, 0 } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Contains("past months are never recalculated", await res.Content.ReadAsStringAsync());
        var json = await (await Send(c, admin, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(json.GetProperty("versions").EnumerateArray());                      // nothing was added
    }

    [RequiresDbFact]
    public async Task Employees_cannot_read_or_change_schedules()
    {
        var (f, c, admin, employee) = await Start(); await using var _f = f;
        var id = await CreateEmployee(c, admin, new { email = NewEmail(), fullName = "Guarded" });

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, employee, HttpMethod.Get, $"/api/admin/employees/{id}/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, employee, HttpMethod.Post, $"/api/admin/employees/{id}/schedule", new { effectiveFrom = "2099-01-01", minutes = new[] { 1, 0, 0, 0, 0, 0, 0 } })).StatusCode);
    }
}
