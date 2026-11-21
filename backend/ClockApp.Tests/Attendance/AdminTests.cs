using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance.Entities;
using ClockApp.Tests.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

/// <summary>
/// Admin corrections (append-only audit trail), shift setting and employee management.
/// Punch times use fixed PAST dates because corrections may not lie in the future (server clock check).
/// </summary>
public sealed class AdminTests : IClassFixture<DatabaseFixture>
{
    private const string Password = "test-password-123";
    private readonly DatabaseFixture _db;

    public AdminTests(DatabaseFixture db) => _db = db;

    private sealed class Ctx
    {
        public required ApiFactory Factory { get; init; }
        public required HttpClient Client { get; init; }
        public required StubTimeSource Time { get; init; }
        public required string AdminToken { get; init; }
        public required string EmployeeToken { get; init; }
        public required int EmployeeId { get; init; }
        public required string EmployeeEmail { get; init; }
    }

    private async Task<User> CreateUserAsync(UserRole role, string name)
    {
        await using var db = _db.CreateContext();
        var user = new User { Email = $"{role}-{Guid.NewGuid():N}@test.local", FullName = name, Role = role };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<string> LoginAsync(HttpClient c, string email)
    {
        var res = await c.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<Ctx> StartAsync()
    {
        var stub = new StubTimeSource();
        var factory = new ApiFactory(_db.ConnectionString) { TimeSource = stub };
        var client = factory.CreateClient();
        var admin = await CreateUserAsync(UserRole.Admin, "Test Admin");
        var emp = await CreateUserAsync(UserRole.Employee, "Test Employee");
        return new Ctx
        {
            Factory = factory, Client = client, Time = stub, EmployeeId = emp.Id, EmployeeEmail = emp.Email,
            AdminToken = await LoginAsync(client, admin.Email), EmployeeToken = await LoginAsync(client, emp.Email),
        };
    }

    private static Task<HttpResponseMessage> Send(Ctx c, string token, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return c.Client.SendAsync(req);
    }

    private Task<HttpResponseMessage> Correct(Ctx c, object body) => Send(c, c.AdminToken, HttpMethod.Post, "/api/admin/attendance/corrections", body);

    /// <summary>Employee punches in/out at the given Zurich winter times (all in the past).</summary>
    private async Task<long> PunchAsync(Ctx c, string inLocal, string? outLocal)
    {
        c.Time.SetZurich(inLocal, dst: false);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/attendance/clock-in")).StatusCode);
        if (outLocal is not null)
        {
            c.Time.SetZurich(outLocal, dst: false);
            Assert.Equal(HttpStatusCode.OK, (await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/attendance/clock-out")).StatusCode);
        }
        await using var db = _db.CreateContext();
        return (await db.AttendanceRecords.Where(r => r.UserId == c.EmployeeId).OrderByDescending(r => r.Id).FirstAsync()).Id;
    }

    private async Task<JsonElement> AdminViewAsync(Ctx c, string from, string to)
    {
        var res = await Send(c, c.AdminToken, HttpMethod.Get, $"/api/admin/attendance?userId={c.EmployeeId}&from={from}&to={to}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [RequiresDbFact]
    public async Task Adjust_keeps_the_original_punch_and_records_previous_values_and_reason()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        var id = await PunchAsync(c, "2026-02-02T09:00:00", "2026-02-02T16:00:00");

        var res = await Correct(c, new { kind = "Adjust", userId = c.EmployeeId, attendanceRecordId = id, clockOutLocal = "2026-02-02T17:00:00", reason = "Left later, forgot to tap" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        await using var db = _db.CreateContext();
        var original = await db.AttendanceRecords.SingleAsync(r => r.Id == id);
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T15:00:00Z"), original.ClockOutUtc); // untouched API time
        var corr = await db.AttendanceCorrections.SingleAsync(x => x.AttendanceRecordId == id);
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T15:00:00Z"), corr.PreviousClockOutUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T16:00:00Z"), corr.CorrectedClockOutUtc);
        Assert.Equal("Left later, forgot to tap", corr.Reason);

        var view = await AdminViewAsync(c, "2026-02-01", "2026-02-28");
        var row = view.GetProperty("records").EnumerateArray().Single();
        Assert.True(row.GetProperty("isAdjusted").GetBoolean());
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T15:00:00Z"), row.GetProperty("original").GetProperty("clockOutUtc").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T16:00:00Z"), row.GetProperty("effective").GetProperty("clockOutUtc").GetDateTimeOffset());
        Assert.Equal("Test Admin", row.GetProperty("history")[0].GetProperty("createdBy").GetString());
    }

    [RequiresDbFact]
    public async Task Admin_view_reports_zurich_now_for_form_defaults()
    {
        // The fake clock stays near the real time so login tokens remain valid; the conversion itself
        // (including the cross-midnight case) is covered by ZurichLocalInputTests.
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var factory = new ApiFactory(_db.ConnectionString, clock: clock);
        await using var _ = factory;
        var client = factory.CreateClient();
        var admin = await CreateUserAsync(UserRole.Admin, "Clock Admin");
        var emp = await CreateUserAsync(UserRole.Employee, "Clock Employee");
        var token = await LoginAsync(client, admin.Email);
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/attendance?userId={emp.Id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var json = await (await client.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();

        var expected = ClockApp.Attendance.Time.ZurichTime.ToLocal(clock.Now).ToString("yyyy-MM-dd'T'HH:mm");
        Assert.Equal(expected, json.GetProperty("zurichNow").GetString());
    }

    [RequiresDbFact]
    public async Task Add_creates_a_manual_record_and_never_touches_punches()
    {
        var c = await StartAsync(); await using var _ = c.Factory;

        var res = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-03T09:00:00", clockOutLocal = "2026-02-03T17:00:00", reason = "Forgot to clock in at all" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        await using var db = _db.CreateContext();
        Assert.Empty(await db.AttendanceRecords.Where(r => r.UserId == c.EmployeeId).ToListAsync());
        var row = (await AdminViewAsync(c, "2026-02-01", "2026-02-28")).GetProperty("records").EnumerateArray().Single();
        Assert.Equal("manual", row.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("original").ValueKind);
    }

    [RequiresDbFact]
    public async Task Correction_without_a_real_reason_is_rejected_and_nothing_is_written()
    {
        var c = await StartAsync(); await using var _ = c.Factory;

        var res = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-03T09:00:00", clockOutLocal = "2026-02-03T17:00:00", reason = "  ok " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        await using var db = _db.CreateContext();
        Assert.Empty(await db.AttendanceCorrections.Where(x => x.UserId == c.EmployeeId).ToListAsync());
    }

    [RequiresDbFact]
    public async Task Closing_a_forgotten_clock_out_lets_the_employee_clock_in_again()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        var id = await PunchAsync(c, "2026-02-04T09:00:00", null); // forgot to clock out

        c.Time.SetZurich("2026-02-05T09:00:00", dst: false);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/attendance/clock-in")).StatusCode);

        var res = await Correct(c, new { kind = "Adjust", userId = c.EmployeeId, attendanceRecordId = id, clockOutLocal = "2026-02-04T17:00:00", reason = "Forgot to clock out" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/attendance/clock-in")).StatusCode);
    }

    [RequiresDbFact]
    public async Task Overlap_with_another_record_is_rejected_but_touching_is_allowed()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        var first = await PunchAsync(c, "2026-02-09T08:00:00", "2026-02-09T12:00:00");
        await PunchAsync(c, "2026-02-09T13:00:00", "2026-02-09T17:00:00");

        var overlap = await Correct(c, new { kind = "Adjust", userId = c.EmployeeId, attendanceRecordId = first, clockOutLocal = "2026-02-09T13:30:00", reason = "Extend into the next shift" });
        var touching = await Correct(c, new { kind = "Adjust", userId = c.EmployeeId, attendanceRecordId = first, clockOutLocal = "2026-02-09T13:00:00", reason = "Ends exactly when next starts" });
        var addInside = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-09T14:00:00", clockOutLocal = "2026-02-09T15:00:00", reason = "Inside an existing shift" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, overlap.StatusCode);
        Assert.Contains("overlap", await overlap.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, touching.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, addInside.StatusCode);
    }

    [RequiresDbFact]
    public async Task Add_overlapping_an_open_record_is_rejected()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        await PunchAsync(c, "2026-02-10T09:00:00", null); // open: [09:00, infinity)

        var res = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-10T12:00:00", clockOutLocal = "2026-02-10T13:00:00", reason = "Overlaps the open shift" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [RequiresDbFact]
    public async Task Invalid_times_are_rejected()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        object Add(string i, string o) => new { kind = "Add", userId = c.EmployeeId, clockInLocal = i, clockOutLocal = o, reason = "Some valid reason" };

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Correct(c, Add("2026-02-11T17:00:00", "2026-02-11T09:00:00"))).StatusCode); // out before in
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Correct(c, Add("2026-02-11T08:00:00", "2026-02-12T09:00:00"))).StatusCode); // > 24 h
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Correct(c, Add("2099-01-01T08:00:00", "2099-01-01T09:00:00"))).StatusCode); // future
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-11T08:00:00Z", clockOutLocal = "2026-02-11T09:00:00Z", reason = "Has a Z designator" })).StatusCode);
    }

    [RequiresDbFact]
    public async Task Dst_nonexistent_and_ambiguous_local_times_are_rejected_unless_the_offset_is_given()
    {
        var c = await StartAsync(); await using var _ = c.Factory;

        var gap = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-03-29T02:30:00", clockOutLocal = "2026-03-29T04:00:00", reason = "Spring forward gap" });
        var ambiguous = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-10-25T02:30:00", clockOutLocal = "2026-10-25T04:00:00", reason = "Fall back ambiguity" });
        var withOffset = await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-10-25T02:30:00", clockInOffsetMinutes = 60, clockOutLocal = "2026-10-25T04:00:00", reason = "Second occurrence of 02:30" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, gap.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ambiguous.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withOffset.StatusCode);
        await using var db = _db.CreateContext();
        var stored = await db.AttendanceCorrections.SingleAsync(x => x.UserId == c.EmployeeId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T01:30:00Z"), stored.CorrectedClockInUtc);
        Assert.Equal(new DateOnly(2026, 10, 25), stored.WorkDate);
    }

    [RequiresDbFact]
    public async Task A_clock_in_inside_a_corrected_interval_is_rejected()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        await Correct(c, new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-12T10:00:00", clockOutLocal = "2026-02-12T11:00:00", reason = "Admin added a meeting block" });

        c.Time.SetZurich("2026-02-12T10:30:00", dst: false);
        var res = await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/attendance/clock-in");

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        await using var db = _db.CreateContext();
        Assert.Empty(await db.AttendanceRecords.Where(r => r.UserId == c.EmployeeId).ToListAsync());
    }

    [RequiresDbFact]
    public async Task Employees_cannot_use_admin_endpoints_and_unknown_targets_are_404()
    {
        var c = await StartAsync(); await using var _ = c.Factory;
        var body = new { kind = "Add", userId = c.EmployeeId, clockInLocal = "2026-02-13T09:00:00", clockOutLocal = "2026-02-13T10:00:00", reason = "Employee tries it" };

        var asEmployee = await Send(c, c.EmployeeToken, HttpMethod.Post, "/api/admin/attendance/corrections", body);
        var unknownRecord = await Correct(c, new { kind = "Adjust", userId = c.EmployeeId, attendanceRecordId = 999999999, clockOutLocal = "2026-02-13T10:00:00", reason = "No such record" });
        var unknownUser = await Correct(c, new { kind = "Add", userId = 999999999, clockInLocal = "2026-02-13T09:00:00", clockOutLocal = "2026-02-13T10:00:00", reason = "No such user" });

        Assert.Equal(HttpStatusCode.Forbidden, asEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRecord.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
    }

    [RequiresDbFact]
    public async Task Shift_duration_can_be_read_and_updated_by_admins_only_within_limits()
    {
        var c = await StartAsync(); await using var _ = c.Factory;

        var read = await Send(c, c.AdminToken, HttpMethod.Get, "/api/admin/settings/shift");
        var set = await Send(c, c.AdminToken, HttpMethod.Put, "/api/admin/settings/shift", new { durationMinutes = 510 });
        var zero = await Send(c, c.AdminToken, HttpMethod.Put, "/api/admin/settings/shift", new { durationMinutes = 0 });
        var asEmployee = await Send(c, c.EmployeeToken, HttpMethod.Put, "/api/admin/settings/shift", new { durationMinutes = 100 });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asEmployee.StatusCode);
        await using var db = _db.CreateContext();
        Assert.Equal(510, (await db.ShiftSettings.SingleAsync()).DurationMinutes);

        // restore the default so other tests/fixtures see a consistent value
        await Send(c, c.AdminToken, HttpMethod.Put, "/api/admin/settings/shift", new { durationMinutes = 480 });
    }

    [RequiresDbFact]
    public async Task Deactivation_blocks_login_and_reissuing_an_invite_returns_a_fresh_link()
    {
        var c = await StartAsync(); await using var _ = c.Factory;

        var list = await (await Send(c, c.AdminToken, HttpMethod.Get, "/api/admin/employees")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetInt32() == c.EmployeeId);

        Assert.Equal(HttpStatusCode.OK, (await Send(c, c.AdminToken, HttpMethod.Post, $"/api/admin/employees/{c.EmployeeId}/deactivate")).StatusCode);
        var login = await c.Client.PostAsJsonAsync("/api/auth/login", new { email = c.EmployeeEmail, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var invite = await Send(c, c.AdminToken, HttpMethod.Post, $"/api/admin/employees/{c.EmployeeId}/invite");
        var json = await invite.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("/set-password?token=", json.GetProperty("invitePath").GetString());
    }
}
