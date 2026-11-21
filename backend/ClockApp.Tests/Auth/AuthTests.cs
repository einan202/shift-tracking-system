using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClockApp.Api.Auth;
using ClockApp.Attendance.Entities;
using ClockApp.Tests.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Auth;

/// <summary>End-to-end auth tests against a real (throw-away) SQL Server database.</summary>
public sealed class AuthTests : IClassFixture<DatabaseFixture>
{
    private const string AdminEmail = "admin@test.local";
    private const string AdminPassword = "admin-password-123";
    private readonly DatabaseFixture _db;

    public AuthTests(DatabaseFixture db) => _db = db;

    // Each test uses its own unique emails so tests can share one database without interfering.
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.local";

    private async Task<User> EnsureAdminAsync(string email = AdminEmail)
    {
        await using var db = _db.CreateContext();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (existing is not null) return existing;
        var admin = new User { Email = email, FullName = "Test Admin", Role = UserRole.Admin };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, AdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Admin creates an employee and returns (email, raw invite token).</summary>
    private async Task<(string Email, string Token)> CreateEmployeeAsync(HttpClient client)
    {
        await EnsureAdminAsync();
        var adminToken = await LoginAsync(client, AdminEmail, AdminPassword);
        var email = Unique("emp");
        var res = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/employees", adminToken, new { email, fullName = "Eve Employee" }));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        // The token travels in the URL fragment (never sent to the server, so not in access logs), not the query string.
        Assert.Equal($"/set-password#token={json.GetProperty("inviteToken").GetString()}", json.GetProperty("invitePath").GetString());
        return (email, json.GetProperty("inviteToken").GetString()!);
    }

    [RequiresDbFact]
    public async Task Login_succeeds_and_token_carries_the_database_role()
    {
        await EnsureAdminAsync();
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();

        var token = await LoginAsync(client, AdminEmail, AdminPassword);
        var me = await client.SendAsync(Authed(HttpMethod.Get, "/api/auth/me", token));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Admin", (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString());
    }

    [RequiresDbFact]
    public async Task Wrong_password_and_unknown_email_give_the_same_generic_401()
    {
        await EnsureAdminAsync();
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = "nope-nope-nope" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "ghost@test.local", password = "nope-nope-nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [RequiresDbFact]
    public async Task Protected_routes_need_a_token_and_admin_routes_need_the_admin_role()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        var (email, invite) = await CreateEmployeeAsync(client);
        await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "employee-pass-123" });
        var employeeToken = await LoginAsync(client, email, "employee-pass-123");

        var anonymous = await client.GetAsync("/api/auth/me");
        var asEmployee = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/employees", employeeToken, new { email = Unique("x"), fullName = "X" }));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asEmployee.StatusCode);
    }

    [RequiresDbFact]
    public async Task Invite_flow_sets_the_password_once_and_stores_only_hashes()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        var (email, invite) = await CreateEmployeeAsync(client);

        // Before the password is set the employee cannot log in.
        var early = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "employee-pass-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);

        var set = await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "employee-pass-123" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        await LoginAsync(client, email, "employee-pass-123"); // now works

        // The same link cannot be used twice.
        var replay = await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "another-pass-123" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // DB holds only hashes: no raw token, no plaintext password, token cleared after use.
        await using var db = _db.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.Null(user.InviteTokenHash);
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("employee-pass-123", user.PasswordHash);
    }

    [RequiresDbFact]
    public async Task Created_invite_is_stored_hashed_not_raw()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        var (email, invite) = await CreateEmployeeAsync(client);

        await using var db = _db.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Email == email);

        Assert.NotEqual(invite, user.InviteTokenHash);
        Assert.Equal(InviteTokens.Hash(invite), user.InviteTokenHash);
        Assert.Null(user.PasswordHash);
    }

    [RequiresDbFact]
    public async Task Expired_invite_is_rejected()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        await using var factory = new ApiFactory(_db.AppConnectionString, clock: clock);
        var client = factory.CreateClient();
        var (_, invite) = await CreateEmployeeAsync(client);

        clock.Now = clock.Now.AddHours(49); // past the 48 h lifetime
        var res = await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "employee-pass-123" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [RequiresDbFact]
    public async Task Short_password_and_unknown_token_are_rejected()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        var (_, invite) = await CreateEmployeeAsync(client);

        var tooShort = await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "short" });
        var unknown = await client.PostAsJsonAsync("/api/auth/set-password", new { token = "not-a-real-token", password = "employee-pass-123" });

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [RequiresDbFact]
    public async Task Deactivated_user_is_locked_out_even_with_a_valid_token()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        var (email, invite) = await CreateEmployeeAsync(client);
        await client.PostAsJsonAsync("/api/auth/set-password", new { token = invite, password = "employee-pass-123" });
        var token = await LoginAsync(client, email, "employee-pass-123");

        await using (var db = _db.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        var me = await client.SendAsync(Authed(HttpMethod.Get, "/api/auth/me", token));
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "employee-pass-123" });

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [RequiresDbFact]
    public async Task Expired_jwt_is_rejected()
    {
        var admin = await EnsureAdminAsync();
        // Token issued two hours ago by the (fake-clock) token service has already expired.
        var old = new FakeClock(DateTimeOffset.UtcNow.AddHours(-2));
        await using var factory = new ApiFactory(_db.AppConnectionString, clock: old);
        var client = factory.CreateClient();
        var token = await LoginAsync(client, AdminEmail, AdminPassword);

        var me = await client.SendAsync(Authed(HttpMethod.Get, "/api/auth/me", token));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.NotNull(admin);
    }

    [RequiresDbFact]
    public async Task Employee_creation_rejects_duplicates_and_bad_input()
    {
        await using var factory = new ApiFactory(_db.AppConnectionString);
        var client = factory.CreateClient();
        await EnsureAdminAsync();
        var admin = await LoginAsync(client, AdminEmail, AdminPassword);
        var email = Unique("dup");

        var first = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/employees", admin, new { email, fullName = "A" }));
        var dup = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/employees", admin, new { email, fullName = "A" }));
        var bad = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/employees", admin, new { email = "not-an-email", fullName = "A" }));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        // No database needed: the limiter rejects before any query runs once the limit is exceeded.
        await using var factory = new ApiFactory(authPerMinute: 3);
        var client = factory.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" })).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, codes[^1]);
        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.Unauthorized));
    }
}
