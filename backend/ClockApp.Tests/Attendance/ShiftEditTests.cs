using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

public sealed class ShiftEditTests : IClassFixture<ClockApp.Tests.Data.DatabaseFixture>
{
    private readonly ClockApp.Tests.Data.DatabaseFixture _db;
    public ShiftEditTests(ClockApp.Tests.Data.DatabaseFixture db) => _db = db;

    // "Now" = Tuesday 2026-02-10 13:00 Zurich (12:00 UTC). Employee works Mon/Tue 8h30.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-02-10T12:00:00Z");

    private sealed record Setup(ApiFactory Factory, HttpClient Client, int UserId, long OpenRecordId, long ClosedRecordId);

    private async Task<Setup> ArrangeAsync()
    {
        var fake = new FakeClock(DateTimeOffset.UtcNow);   // real time while logging in so the JWT is valid
        var factory = new ApiFactory(_db.AppConnectionString, clock: fake);
        var client = factory.CreateClient();
        await using var db = _db.CreateContext();
        var admin = new User { Email = $"sa-{Guid.NewGuid():N}@t.local", FullName = "A", Role = UserRole.Admin };
        var emp = new User { Email = $"se-{Guid.NewGuid():N}@t.local", FullName = "E", Role = UserRole.Employee, EmploymentStartDate = new DateOnly(2026, 2, 1) };
        emp.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(emp, "test-password-123");
        db.Users.AddRange(admin, emp);
        await db.SaveChangesAsync();
        db.EmployeeScheduleVersions.Add(new EmployeeScheduleVersion { UserId = emp.Id, EffectiveFrom = new DateOnly(2026, 2, 1), ExpectedMinutesMon = 510, ExpectedMinutesTue = 510, CreatedByUserId = admin.Id, CreatedAtUtc = Now });
        // Monday 2026-02-02 closed 08:00-12:00 Zurich; Tuesday 2026-02-03 clocked in 08:00 Zurich and never clocked out.
        var closed = new AttendanceRecord { UserId = emp.Id, ClockInUtc = DateTimeOffset.Parse("2026-02-02T07:00:00Z"), ClockOutUtc = DateTimeOffset.Parse("2026-02-02T11:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "stub" };
        var open = new AttendanceRecord { UserId = emp.Id, ClockInUtc = DateTimeOffset.Parse("2026-02-03T07:00:00Z"), WorkDate = new DateOnly(2026, 2, 3), IsOpen = true, TimeProvider = "stub" };
        db.AttendanceRecords.AddRange(closed, open);
        await db.SaveChangesAsync();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = emp.Email, password = "test-password-123" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        fake.Now = Now;                                    // then pin "now" for the edits
        return new Setup(factory, client, emp.Id, open.Id, closed.Id);
    }

    private static object Edit(string date, string? inLocal, string? outLocal, string code = "ForgotClockOut", string? text = null) =>
        new { date, clockInLocal = inLocal, clockOutLocal = outLocal, reasonCode = code, reasonText = text };

    private async Task<int> CorrectionCountAsync(int userId)
    {
        await using var db = _db.CreateContext();
        return await db.AttendanceCorrections.CountAsync(c => c.UserId == userId);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Employee_closes_a_forgotten_clock_out_which_frees_the_open_record_and_keeps_the_punch()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;

        var r = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-03", null, "2026-02-03T17:00:00"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var day = body.GetProperty("after").GetProperty("day");
        Assert.Equal(9 * 60, day.GetProperty("workedMinutes").GetInt64());
        Assert.True(day.GetProperty("clockOutIsManual").GetBoolean());
        Assert.False(day.GetProperty("clockInIsManual").GetBoolean());   // only the edited field is manual

        await using var db = _db.CreateContext();
        var rec = await db.AttendanceRecords.SingleAsync(x => x.Id == s.OpenRecordId);
        Assert.False(rec.IsOpen);                      // can clock in again
        Assert.Null(rec.ClockOutUtc);                  // the punch itself is untouched
        var row = await db.AttendanceCorrections.SingleAsync(c => c.UserId == s.UserId);
        Assert.Equal(CorrectionKind.Adjust, row.Kind);
        Assert.Equal(ReasonCodes.ForgotClockOut, row.ReasonCode);
        Assert.Equal(s.UserId, row.CreatedByUserId);   // the employee is the actor
        Assert.Equal(Now, row.CreatedAtUtc);
        Assert.NotNull(row.BatchId);
        Assert.Equal(DateTimeOffset.Parse("2026-02-03T16:00:00Z"), row.CorrectedClockOutUtc);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Employee_adds_a_missing_day_and_edits_the_clock_in_of_an_existing_one()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;

        // The open Tuesday shift counts to infinity, so no day after it can be added until it is closed.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-09", "2026-02-09T08:00:00", "2026-02-09T16:30:00", "ForgotClockIn"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-03", null, "2026-02-03T17:00:00"))).StatusCode);

        // 2026-02-09 (Monday) has no shift: both times are required.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-09", "2026-02-09T08:00:00", null, "ForgotClockIn"))).StatusCode);
        var add = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-09", "2026-02-09T08:00:00", "2026-02-09T16:30:00", "ForgotClockIn"));
        Assert.True(add.StatusCode == HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        var added = (await add.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("after").GetProperty("day");
        Assert.Equal(0, added.GetProperty("balanceMinutes").GetInt64());
        Assert.True(added.GetProperty("clockInIsManual").GetBoolean() && added.GetProperty("clockOutIsManual").GetBoolean());

        // Monday 2026-02-02: the clock-in was really 07:00.
        var fix = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-02", "2026-02-02T07:00:00", null, "WrongTimePunched"));
        Assert.True(fix.StatusCode == HttpStatusCode.OK, await fix.Content.ReadAsStringAsync());
        var fixedDay = (await fix.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("after").GetProperty("day");
        Assert.Equal(5 * 60, fixedDay.GetProperty("workedMinutes").GetInt64());
        Assert.True(fixedDay.GetProperty("clockInIsManual").GetBoolean());
        Assert.False(fixedDay.GetProperty("clockOutIsManual").GetBoolean());
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Preview_writes_nothing_and_shows_the_same_effective_result_as_the_commit()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;
        var edit = Edit("2026-02-03", null, "2026-02-03T17:00:00");

        var preview = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/preview", edit);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(0, await CorrectionCountAsync(s.UserId));
        var p = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(p.GetProperty("saved").GetBoolean());
        Assert.True(p.GetProperty("before").GetProperty("day").GetProperty("inProgress").GetBoolean());

        var commit = await (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", edit)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(p.GetProperty("after").GetRawText(), commit.GetProperty("after").GetRawText());
        Assert.Equal(1, await CorrectionCountAsync(s.UserId));

        // The employee response never exposes reasons, history or providers.
        var text = commit.GetRawText().ToLowerInvariant();
        Assert.DoesNotContain("reason", text);
        Assert.DoesNotContain("history", text);
        Assert.DoesNotContain("provider", text);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Invalid_edits_are_rejected_and_write_nothing()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;
        (string Date, string? In, string? Out, string Code, string? Text)[] cases =
        [
            ("2026-02-03", null, "2026-02-03T17:00:00", "Nonsense", null),             // unknown reason
            ("2026-02-03", null, "2026-02-03T17:00:00", "Other", "abc"),               // Other needs real text
            ("2026-02-03", null, "2026-02-03T07:00:00", "ForgotClockOut", null),       // out before in (08:00)
            ("2026-02-03", null, "2026-02-04T09:00:00", "ForgotClockOut", null),       // 25 h
            ("2026-02-10", "2026-02-10T08:00:00", "2026-02-10T18:00:00", "ForgotClockIn", null),   // clock-out in the future (now 13:00)
            ("2026-02-09", "2026-02-10T08:00:00", "2026-02-10T09:00:00", "ForgotClockIn", null),   // clock-in not on the edited day
        ];

        foreach (var c in cases)
        {
            var r = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit(c.Date, c.In, c.Out, c.Code, c.Text));
            Assert.True(r.StatusCode == HttpStatusCode.UnprocessableEntity, $"{c} -> {(int)r.StatusCode}");
        }
        Assert.Equal(0, await CorrectionCountAsync(s.UserId));
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Overlapping_another_shift_is_rejected_and_an_employee_cannot_reach_other_users()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;

        // Make Tuesday's open shift (from 08:00) collide with a new manual Monday shift that ends after 08:00 Tuesday: not allowed by the day check.
        var overlap = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-02", null, "2026-02-03T09:00:00", "ForgotClockOut"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overlap.StatusCode);   // >24 h or overlaps Tuesday's open shift
        Assert.Equal(0, await CorrectionCountAsync(s.UserId));

        // The body has no user id: edits always target the signed-in employee, and anonymous callers are refused.
        var anon = s.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-03", null, "2026-02-03T17:00:00"))).StatusCode);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task The_reasons_catalog_lists_the_seven_standard_reasons()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;

        var list = await s.Client.GetFromJsonAsync<JsonElement>("/api/attendance/reasons");

        Assert.Equal(ReasonCodes.All.OrderBy(x => x), list.EnumerateArray().Select(e => e.GetProperty("code").GetString()!).OrderBy(x => x));
    }
}
