using ClockApp.Attendance.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClockApp.Tests;

/// <summary>A TimeProvider whose "now" tests can set (used for token / invite expiry, never attendance).</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Test host: real app + fixed test settings. Pass a connection string for DB-backed tests.</summary>
public sealed class ApiFactory(string? connectionString = null, int authPerMinute = 1000, FakeClock? clock = null)
    : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "test-only-secret-test-only-secret-0123456789";

    public IAttendanceTimeSource? TimeSource { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("RateLimit:AuthPerMinute", authPerMinute.ToString());
        builder.UseSetting("ConnectionStrings:Default", connectionString ?? "Server=unused;Database=unused");
        builder.ConfigureServices(services =>
        {
            if (clock is not null) services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            if (TimeSource is not null) services.Replace(ServiceDescriptor.Singleton(TimeSource));
        });
    }
}
