using System.Security.Claims;
using System.Text;
using ClockApp.Attendance.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClockApp.Api.Auth;

/// <summary>
/// Issues signed JWT access tokens. This is authentication, not attendance recording, so the
/// (injectable) system clock is allowed here; it lives in ClockApp.Api, never in ClockApp.Attendance.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    // Claim names are kept literal (MapInboundClaims is disabled in Program) so there is no hidden remapping.
    public const string RoleClaim = "role";
    public const string SubjectClaim = "sub";

    public (string Token, DateTimeOffset ExpiresUtc) Issue(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(SubjectClaim, user.Id.ToString()),
                new Claim(RoleClaim, user.Role.ToString()),
            ]),
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
