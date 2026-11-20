using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Data;

public static class DatabaseInitializer
{
    /// <summary>Applies migrations and seeds the first admin when SeedAdmin settings are provided.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await db.Database.MigrateAsync();

        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("SeedAdmin not configured; skipping admin seeding.");
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var created = await AdminSeeder.SeedAsync(db, hasher, email, password);
        logger.LogInformation(created ? "Seeded the first admin account." : "An admin already exists; seeding skipped.");
    }
}
