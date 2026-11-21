using System.Text;
using System.Threading.RateLimiting;
using ClockApp.Api.Admin;
using ClockApp.Api.Attendance;
using ClockApp.Api.Auth;
using ClockApp.Api.Data;
using ClockApp.Api.Time;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Resolved lazily from the final configuration so integration tests can override settings.
builder.Services.AddDbContext<AttendanceDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<CorrectionService>();

// System clock: used ONLY for authentication (JWT / invite expiry). Attendance time never comes from it.
builder.Services.AddSingleton(TimeProvider.System);

// ---- Authentication (JWT) -------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .Validate(o => o.Secret.Length >= JwtOptions.MinSecretLength,
        $"Jwt:Secret must be at least {JwtOptions.MinSecretLength} characters (set Jwt__Secret).")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        var o = jwt.Value;
        bearer.MapInboundClaims = false; // keep claim names exactly as issued ("sub", "role")
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = o.Issuer,
            ValidAudience = o.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = TokenService.SubjectClaim,
            RoleClaimType = TokenService.RoleClaim,
        };
        // Per-request check: a deactivated user, or one whose role changed, is locked out immediately
        // even though the token itself is still cryptographically valid.
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var id = ctx.Principal?.FindFirst(TokenService.SubjectClaim)?.Value;
                var role = ctx.Principal?.FindFirst(TokenService.RoleClaim)?.Value;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AttendanceDbContext>();
                var current = int.TryParse(id, out var userId)
                    ? await db.Users.AsNoTracking().Where(u => u.Id == userId)
                        .Select(u => new { u.IsActive, u.Role }).SingleOrDefaultAsync()
                    : null;
                if (current is null || !current.IsActive || current.Role.ToString() != role)
                    ctx.Fail("User is inactive, unknown or its role changed.");
            },
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)))
    .AddPolicy(Policies.Employee, p => p.RequireRole(nameof(UserRole.Employee)));

// ---- Rate limiting for unauthenticated auth endpoints (slows password guessing) ----------------
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthEndpoints.RateLimitPolicy, ctx =>
    {
        var perMinute = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimit:AuthPerMinute", 10);
        // Partitioned by client IP (real IP via X-Forwarded-For from the nginx container).
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
    });
});

// The backend is only reachable through the nginx container, so its X-Forwarded-For is trusted.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// External Europe/Zurich time API: the only time source for Clock In/Out records.
builder.Services.AddHttpClient<IAttendanceTimeSource, TimeApiIoTimeSource>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["TimeApi:BaseUrl"] ?? "https://timeapi.io/");
    client.Timeout = TimeSpan.FromSeconds(3);
});

var app = builder.Build();

// Opt-in (docker compose sets it): apply migrations and seed the first admin at startup.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapEmployeeEndpoints();
app.MapAttendanceEndpoints();
app.MapMonthlyReportEndpoint();
app.MapSettingsEndpoints();
app.MapAdminAttendanceEndpoints();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
