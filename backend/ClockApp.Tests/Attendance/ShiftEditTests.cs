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

    private sealed record Setup(FakeClock Fake, ApiFactory Factory, HttpClient Client, int UserId, long OpenRecordId, long ClosedRecordId);

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
        return new Setup(fake, factory, client, emp.Id, open.Id, closed.Id);
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
            ("2026-01-30", "2026-01-30T08:00:00", "2026-01-30T16:00:00", "ForgotClockIn", null),   // before the employment start date (2026-02-01)
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
    public async Task Error_names_the_existing_time_it_conflicts_with()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;

        // Monday is 08:00-12:00. A new clock-in of 13:00 collides with the CURRENT clock-out.
        var r = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-02", "2026-02-02T13:00:00", null, "WrongTimePunched"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("current clock-out 12:00", await r.Content.ReadAsStringAsync());

        // A new clock-out of 07:00 is before the existing clock-in.
        var r2 = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-02", null, "2026-02-02T07:00:00", "WrongTimePunched"));
        var m2 = await r2.Content.ReadAsStringAsync();
        Assert.Contains("new clock-out 07:00 is earlier than your clock-in 08:00; enter a time after 08:00", m2);
        Assert.Contains("(Your current clock-out is 12:00.)", m2);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Employee_deletes_a_shift_with_a_reason_but_not_an_open_one_and_can_re_add_it()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;
        object Del(string d, string code = "DuplicateOrAccidental") => new { date = d, reasonCode = code };

        // Tuesday is still open: blocked, nothing written.
        var open = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete", Del("2026-02-03"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, open.StatusCode);
        // An empty day and a missing reason are rejected too.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete", Del("2026-02-09"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete", Del("2026-02-02", "Other"))).StatusCode);
        Assert.Equal(0, await CorrectionCountAsync(s.UserId));

        // Preview writes nothing; commit voids Monday; the punch row stays.
        var prev = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete/preview", Del("2026-02-02"));
        Assert.Equal(HttpStatusCode.OK, prev.StatusCode);
        Assert.Equal(0, await CorrectionCountAsync(s.UserId));
        var del = await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete", Del("2026-02-02"));
        var after = (await del.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("after").GetProperty("day");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("clockInUtc").ValueKind);
        await using (var db = _db.CreateContext())
        {
            var row = await db.AttendanceCorrections.SingleAsync(c => c.UserId == s.UserId);
            Assert.Equal(CorrectionKind.Void, row.Kind);
            Assert.Equal(s.ClosedRecordId, row.AttendanceRecordId);
            Assert.Equal(ReasonCodes.DuplicateOrAccidental, row.ReasonCode);
            Assert.NotNull(row.BatchId);
            Assert.True(await db.AttendanceRecords.AnyAsync(r => r.Id == s.ClosedRecordId));
        }

        // Re-adding the day is allowed afterwards.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", Edit("2026-02-02", "2026-02-02T08:00:00", "2026-02-02T16:00:00", "ForgotClockIn"))).StatusCode);
    }

    [ClockApp.Tests.Data.RequiresDbFact]
    public async Task Admin_history_of_a_manual_shift_lists_the_add_and_every_later_correction()
    {
        var s = await ArrangeAsync();
        await using var _ = s.Factory;
        async Task Ok(object body) => Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts", body)).StatusCode);
        await Ok(Edit("2026-02-03", null, "2026-02-03T17:00:00"));                                                   // closes the open shift
        await Ok(Edit("2026-02-09", "2026-02-09T08:00:00", "2026-02-09T16:00:00", "ForgotClockIn"));                  // adds a manual shift
        await Ok(Edit("2026-02-09", null, "2026-02-09T17:00:00", "WrongTimePunched"));                                // corrects its clock-out

        var admin = new User { Email = $"ha-{Guid.NewGuid():N}@t.local", FullName = "Hist Admin", Role = UserRole.Admin };
        admin.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(admin, "test-password-123");
        await using (var db = _db.CreateContext()) { db.Users.Add(admin); await db.SaveChangesAsync(); }
        var ac = s.Factory.CreateClient();
        s.Fake.Now = DateTimeOffset.UtcNow;   // token lifetime is checked against real time
        var login = await ac.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password = "test-password-123" });
        ac.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!);

        var res = await ac.GetFromJsonAsync<JsonElement>($"/api/admin/attendance?userId={s.UserId}&from=2026-02-01&to=2026-02-28");
        var manual = res.GetProperty("records").EnumerateArray().Single(r => r.GetProperty("source").GetString() == "manual");
        var kinds = manual.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToArray();
        Assert.Equal(new string?[] { "Add", "Adjust" }, kinds);   // both changes, in order

        // Deleting the shift keeps it visible to the admin: flagged as deleted, with the Void in its history.
        s.Fake.Now = Now;
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync("/api/attendance/me/shifts/delete", new { date = "2026-02-09", reasonCode = "DuplicateOrAccidental" })).StatusCode);
        s.Fake.Now = DateTimeOffset.UtcNow;
        var after = await ac.GetFromJsonAsync<JsonElement>($"/api/admin/attendance?userId={s.UserId}&from=2026-02-01&to=2026-02-28");
        var gone = after.GetProperty("records").EnumerateArray().Single(r => r.GetProperty("source").GetString() == "manual");
        Assert.True(gone.GetProperty("isVoided").GetBoolean());
        Assert.Equal(new string?[] { "Add", "Adjust", "Void" }, gone.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToArray());
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
