using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

public sealed class NonWorkingDayClockTests : IClassFixture<ClockApp.Tests.Data.DatabaseFixture>
{
    private readonly ClockApp.Tests.Data.DatabaseFixture _db;
    public NonWorkingDayClockTests(ClockApp.Tests.Data.DatabaseFixture db) => _db = db;

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Clock_in_is_refused_on_a_non_working_day_but_clock_out_of_an_open_shift_still_works()
    {
        var stub = new StubTimeSource();
        await using var factory = new ApiFactory(_db.AppConnectionString) { TimeSource = stub };
        var client = factory.CreateClient();
        int userId; string email;
        await using (var db = _db.CreateContext())
        {
            var emp = new User { Email = $"nw-{Guid.NewGuid():N}@t.local", FullName = "E", Role = UserRole.Employee, EmploymentStartDate = new DateOnly(2026, 1, 1) };
            emp.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(emp, "test-password-123");
            db.Users.Add(emp); await db.SaveChangesAsync();
            userId = emp.Id; email = emp.Email;
            db.EmployeeScheduleVersions.Add(new EmployeeScheduleVersion { UserId = userId, EffectiveFrom = new DateOnly(2026, 1, 1), ExpectedMinutesMon = 480, ExpectedMinutesTue = 480, ExpectedMinutesWed = 480, ExpectedMinutesThu = 480, ExpectedMinutesFri = 480, CreatedByUserId = userId, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "test-password-123" });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!);

        stub.SetZurich("2026-06-06T09:00:00", dst: true);   // Saturday
        var refused = await client.PostAsync("/api/attendance/clock-in", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        await using (var db = _db.CreateContext()) Assert.Empty(await db.AttendanceRecords.Where(r => r.UserId == userId).ToListAsync());

        stub.SetZurich("2026-06-05T17:00:00", dst: true);   // Friday: allowed
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/attendance/clock-in", null)).StatusCode);

        stub.SetZurich("2026-06-06T01:00:00", dst: true);   // past midnight into Saturday: clock-out must still work
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/attendance/clock-out", null)).StatusCode);
    }
}
