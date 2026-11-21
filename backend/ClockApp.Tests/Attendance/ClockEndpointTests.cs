using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using ClockApp.Tests.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

/// <summary>A controllable stand-in for the external time API (the only time source for punches).</summary>
public sealed class StubTimeSource : IAttendanceTimeSource
{
    public AttendanceTime? Next { get; set; }
    public bool Down { get; set; }
    public int Calls;

    public Task<AttendanceTime> GetNowAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        if (Down || Next is null) throw new TimeSourceException("stub time API is down");
        return Task.FromResult(Next);
    }

    /// <summary>Sets the stub to a Zurich local time (with the DST flag the real API would report).</summary>
    public void SetZurich(string local, bool dst) =>
        Next = ZurichTime.FromLocal(DateTime.Parse(local), dst, "stub");
}

/// <summary>
/// Clock In/Out behaviour with a stub time API: proves recorded times come from the API only,
/// failures write nothing (503), and double/concurrent punches resolve to exactly one winner.
/// </summary>
public sealed class ClockEndpointTests : IClassFixture<DatabaseFixture>
{
    private const string Password = "employee-pass-123";
    private readonly DatabaseFixture _db;

    public ClockEndpointTests(DatabaseFixture db) => _db = db;

    private sealed record Session(HttpClient Client, string Token, int UserId, StubTimeSource Time);

    /// <summary>A fresh employee (unique, so tests don't interfere), logged in against a stub time API.</summary>
    private async Task<(Session s, ApiFactory f)> StartAsync(string role = "Employee")
    {
        var stub = new StubTimeSource();
        var factory = new ApiFactory(_db.AppConnectionString) { TimeSource = stub };
        var client = factory.CreateClient();

        var email = $"clock-{Guid.NewGuid():N}@test.local";
        int id;
        await using (var db = _db.CreateContext())
        {
            var user = new User { Email = email, FullName = "Clock Tester", Role = Enum.Parse<UserRole>(role) };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        }
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (new Session(client, token, id, stub), factory);
    }

    private static Task<HttpResponseMessage> Post(Session s, string url, HttpContent? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        return s.Client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Get(Session s, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        return s.Client.SendAsync(req);
    }

    private async Task<List<AttendanceRecord>> RecordsAsync(int userId)
    {
        await using var db = _db.CreateContext();
        return await db.AttendanceRecords.Where(r => r.UserId == userId).OrderBy(r => r.ClockInUtc).ToListAsync();
    }

    [RequiresDbFact]
    public async Task Clock_in_stores_the_api_time_not_the_machine_time()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true); // far in the future: cannot be the real clock

        var res = await Post(s, "/api/attendance/clock-in");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(DateTimeOffset.Parse("2031-07-04T07:00:00Z"), rec.ClockInUtc); // 09:00 CEST = 07:00 UTC
        Assert.Equal(new DateOnly(2031, 7, 4), rec.WorkDate);
        Assert.Equal("stub", rec.TimeProvider);
        Assert.True(rec.IsOpen);
    }

    [RequiresDbFact]
    public async Task Forged_timestamps_in_body_query_or_headers_are_ignored()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);

        var forged = JsonContent.Create(new { clockInUtc = "2000-01-01T00:00:00Z", timestamp = "2000-01-01T00:00:00Z" });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/attendance/clock-in?clockInUtc=2000-01-01T00:00:00Z") { Content = forged };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        req.Headers.Date = DateTimeOffset.Parse("2000-01-01T00:00:00Z");
        req.Headers.Add("X-Client-Time", "2000-01-01T00:00:00Z");
        var res = await s.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(DateTimeOffset.Parse("2031-07-04T07:00:00Z"), rec.ClockInUtc);
    }

    [RequiresDbFact]
    public async Task Time_api_down_gives_503_and_writes_nothing_then_a_retry_succeeds()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.Down = true;

        var failed = await Post(s, "/api/attendance/clock-in");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.True(failed.Headers.Contains("Retry-After"));
        Assert.Empty(await RecordsAsync(s.UserId)); // fail closed: no fallback clock, no row

        s.Time.Down = false;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);
        Assert.Equal(HttpStatusCode.OK, (await Post(s, "/api/attendance/clock-in")).StatusCode);
        Assert.Single(await RecordsAsync(s.UserId));
    }

    [RequiresDbFact]
    public async Task Time_api_down_on_clock_out_leaves_the_record_open()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);
        await Post(s, "/api/attendance/clock-in");

        s.Time.Down = true;
        var res = await Post(s, "/api/attendance/clock-out");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.True(rec.IsOpen);
        Assert.Null(rec.ClockOutUtc);
    }

    [RequiresDbFact]
    public async Task Clock_out_stores_the_api_time_and_closes_the_record()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);
        await Post(s, "/api/attendance/clock-in");
        s.Time.SetZurich("2031-07-04T17:30:00", dst: true);

        var res = await Post(s, "/api/attendance/clock-out");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(DateTimeOffset.Parse("2031-07-04T15:30:00Z"), rec.ClockOutUtc);
        Assert.False(rec.IsOpen);
    }

    [RequiresDbFact]
    public async Task Clock_in_twice_and_clock_out_without_clock_in_are_409()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);

        var outFirst = await Post(s, "/api/attendance/clock-out");
        var in1 = await Post(s, "/api/attendance/clock-in");
        var in2 = await Post(s, "/api/attendance/clock-in");

        Assert.Equal(HttpStatusCode.Conflict, outFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, in1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, in2.StatusCode);
        Assert.Single(await RecordsAsync(s.UserId));
    }

    [RequiresDbFact]
    public async Task Concurrent_clock_ins_create_exactly_one_record()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post(s, "/api/attendance/clock-in")));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await RecordsAsync(s.UserId));
    }

    [RequiresDbFact]
    public async Task Concurrent_clock_outs_one_wins_and_the_time_is_never_overwritten()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);
        await Post(s, "/api/attendance/clock-in");
        s.Time.SetZurich("2031-07-04T17:00:00", dst: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post(s, "/api/attendance/clock-out")));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(DateTimeOffset.Parse("2031-07-04T15:00:00Z"), rec.ClockOutUtc);

        // A later, different API time must not overwrite the stored clock-out.
        s.Time.SetZurich("2031-07-04T18:00:00", dst: true);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(s, "/api/attendance/clock-out")).StatusCode);
        Assert.Equal(DateTimeOffset.Parse("2031-07-04T15:00:00Z"), (await RecordsAsync(s.UserId))[0].ClockOutUtc);
    }

    [RequiresDbFact]
    public async Task Clock_out_time_not_after_clock_in_is_rejected_and_the_record_stays_open()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);
        await Post(s, "/api/attendance/clock-in");

        // Provider skew: the "now" returned for clock-out is the same instant as the clock-in.
        var res = await Post(s, "/api/attendance/clock-out");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.True((await RecordsAsync(s.UserId))[0].IsOpen);
    }

    [RequiresDbFact]
    public async Task Status_is_the_source_of_truth_after_a_lost_response()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-04T09:00:00", dst: true);

        var before = await (await Get(s, "/api/attendance/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(before.GetProperty("clockedIn").GetBoolean());

        // The server commits the punch but the client "never saw" the response; it asks /status instead of guessing.
        await Post(s, "/api/attendance/clock-in");
        var after = await (await Get(s, "/api/attendance/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(after.GetProperty("clockedIn").GetBoolean());
        Assert.Equal("stub", after.GetProperty("record").GetProperty("provider").GetString());

        // A blind retry is a clean 409 (no duplicate row).
        Assert.Equal(HttpStatusCode.Conflict, (await Post(s, "/api/attendance/clock-in")).StatusCode);
        Assert.Single(await RecordsAsync(s.UserId));
    }

    [RequiresDbFact]
    public async Task Status_does_not_call_the_time_api()
    {
        var (s, f) = await StartAsync(); await using var _ = f;

        await Get(s, "/api/attendance/status");

        Assert.Equal(0, s.Time.Calls);
    }

    [RequiresDbFact]
    public async Task Work_date_is_the_zurich_date_even_when_utc_is_still_the_previous_day()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        s.Time.SetZurich("2031-07-01T00:30:00", dst: true); // 22:30 UTC on 30 June

        await Post(s, "/api/attendance/clock-in");

        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(DateTimeOffset.Parse("2031-06-30T22:30:00Z"), rec.ClockInUtc);
        Assert.Equal(new DateOnly(2031, 7, 1), rec.WorkDate);
    }

    [RequiresDbFact]
    public async Task Fall_back_night_shift_duration_is_the_real_elapsed_time()
    {
        var (s, f) = await StartAsync(); await using var _ = f;
        // 2031-10-26 is the fall-back day: 02:00-03:00 occurs twice. 22:00 (CEST) -> 06:00 (CET) is 9 real hours.
        s.Time.SetZurich("2031-10-25T22:00:00", dst: true);
        await Post(s, "/api/attendance/clock-in");
        s.Time.SetZurich("2031-10-26T06:00:00", dst: false);
        await Post(s, "/api/attendance/clock-out");

        var rec = Assert.Single(await RecordsAsync(s.UserId));
        Assert.Equal(TimeSpan.FromHours(9), rec.ClockOutUtc - rec.ClockInUtc);
        Assert.Equal(new DateOnly(2031, 10, 25), rec.WorkDate); // counted on the day the shift started
    }

    [RequiresDbFact]
    public async Task Admins_and_anonymous_callers_cannot_punch()
    {
        var (admin, f) = await StartAsync("Admin"); await using var _ = f;
        admin.Time.SetZurich("2031-07-04T09:00:00", dst: true);

        var asAdmin = await Post(admin, "/api/attendance/clock-in");
        var anon = await admin.Client.PostAsync("/api/attendance/clock-in", null);

        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Empty(await RecordsAsync(admin.UserId));
    }
}
