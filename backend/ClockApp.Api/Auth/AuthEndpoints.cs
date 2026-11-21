using System.Security.Claims;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Auth;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record SetPasswordRequest(string? Token, string? Password);

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    public const int MinPasswordLength = 10;

    // A valid-format hash of a random password: verified against when the email is unknown so that
    // "no such user" costs the same time as "wrong password" (no user enumeration by timing).
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString());

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/set-password", SetPasswordAsync).RequireRateLimiting(RateLimitPolicy);
        group.MapGet("/me", (ClaimsPrincipal p) => Results.Ok(new
        {
            id = p.FindFirstValue(TokenService.SubjectClaim),
            role = p.FindFirstValue(TokenService.RoleClaim),
        })).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest req, AttendanceDbContext db, IPasswordHasher<User> hasher, TokenService tokens)
    {
        // One generic answer for every failure: never reveal whether the email exists / is active / has a password.
        var invalid = Results.Json(new { error = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password)) return invalid;

        var email = req.Email.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        // Users who have not yet set a password (PasswordHash null) or are deactivated cannot log in.
        if (user is null || user.PasswordHash is null || !user.IsActive)
        {
            hasher.VerifyHashedPassword(new User(), DummyHash, req.Password);
            return invalid;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed) return invalid;

        // Upgrade the stored hash transparently when the hasher's parameters have been strengthened.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, req.Password);
            await db.SaveChangesAsync();
        }

        var (token, expires) = tokens.Issue(user);
        return Results.Ok(new { token, expiresUtc = expires, role = user.Role.ToString(), fullName = user.FullName });
    }

    private static async Task<IResult> SetPasswordAsync(
        SetPasswordRequest req, AttendanceDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return Results.BadRequest(new { error = "Token is required." });
        if (req.Password is null || req.Password.Length < MinPasswordLength)
            return Results.BadRequest(new { error = $"Password must be at least {MinPasswordLength} characters." });

        // The same answer for unknown, expired and already-used tokens.
        var invalid = Results.BadRequest(new { error = "This link is invalid or has expired. Ask an administrator for a new one." });

        var hash = InviteTokens.Hash(req.Token);
        var user = await db.Users.SingleOrDefaultAsync(u => u.InviteTokenHash == hash);
        if (user is null || !user.IsActive || user.InviteExpiresUtc is null || user.InviteExpiresUtc <= clock.GetUtcNow())
            return invalid;

        user.PasswordHash = hasher.HashPassword(user, req.Password);
        // Single use: clearing the token makes a replay of the same link fail.
        user.InviteTokenHash = null;
        user.InviteExpiresUtc = null;
        await db.SaveChangesAsync();

        return Results.Ok(new { message = "Password set. You can now log in." });
    }
}
