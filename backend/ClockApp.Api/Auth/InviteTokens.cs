using System.Security.Cryptography;
using System.Text;
using ClockApp.Attendance.Entities;

namespace ClockApp.Api.Auth;

/// <summary>
/// One-time invite / reset tokens. The raw token is shown to the admin exactly once; only its SHA-256
/// is stored, so a database leak exposes neither passwords nor usable tokens. A fast hash is fine here
/// because the token is 256 bits of randomness (unlike a human-chosen password).
/// </summary>
public static class InviteTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    /// <summary>Sets a fresh token on the user and returns the raw token (the only time it exists in clear).</summary>
    public static string Issue(User user, DateTimeOffset nowUtc)
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        user.InviteTokenHash = Hash(raw);
        user.InviteExpiresUtc = nowUtc + Lifetime;
        return raw;
    }

    /// <summary>Lower-case hex SHA-256 (64 chars, matches the InviteTokenHash column).</summary>
    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
