using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Startup work that needs elevated rights: apply migrations, provision the least-privilege application user
    /// and seed the first admin. It uses the "Migration" connection string (privileged), falling back to "Default".
    /// The running application itself only ever uses "Default" (the least-privilege user).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var appCs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
        var migrationCs = config.GetConnectionString("Migration") ?? appCs;

        try
        {
            await InitializeCoreAsync(services, config, logger, appCs, migrationCs);
        }
        finally
        {
            // Do not keep idle privileged connections in the pool once startup work is done.
            SqlConnection.ClearAllPools();
        }
    }

    private static async Task InitializeCoreAsync(IServiceProvider services, IConfiguration config, ILogger logger, string appCs, string migrationCs)
    {
        await using var db = new AttendanceDbContext(new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlServer(migrationCs).Options);
        await db.Database.MigrateAsync();

        // Provision the application user when it is a different identity than the migration one.
        if (!string.Equals(new SqlConnectionStringBuilder(migrationCs).UserID, new SqlConnectionStringBuilder(appCs).UserID, StringComparison.OrdinalIgnoreCase))
            await AppUserProvisioner.EnsureAsync(migrationCs, appCs, logger);
        else
            logger.LogWarning("The application connects with the same database user as the migrations; set ConnectionStrings:Migration to use a least-privilege application user.");

        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("SeedAdmin not configured; skipping admin seeding.");
            return;
        }

        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var created = await AdminSeeder.SeedAsync(db, hasher, email, password);
        logger.LogInformation(created ? "Seeded the first admin account." : "An admin already exists; seeding skipped.");

        // Example employees only on the very first start (the admin was just created), and only when enabled.
        if (created && config.GetValue<bool>("SeedDemo:Employees"))
        {
            var adminId = (await db.Users.AsNoTracking().FirstAsync(u => u.Role == UserRole.Admin)).Id;
            var n = await DemoEmployeeSeeder.SeedAsync(db, adminId, TimeProvider.System.GetUtcNow());
            logger.LogInformation("Seeded {Count} example employees (SeedDemo:Employees).", n);
        }
    }
}
