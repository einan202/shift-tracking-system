using ClockApp.Api.Data;
using ClockApp.Attendance;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

/// <summary>Skips a test when no SQL Server is configured (set TEST_DB_CONNECTION; docker compose does this).</summary>
public sealed class RequiresDbFactAttribute : FactAttribute
{
    public RequiresDbFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(DatabaseFixture.ServerConnection))
            Skip = "TEST_DB_CONNECTION is not set; run `docker compose run --rm backend-tests`.";
    }
}

/// <summary>Creates a throw-away database, applies the real migrations, and drops it afterwards.</summary>
public sealed class DatabaseFixture : IDisposable
{
    public static string? ServerConnection => Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");

    // Least-privilege application login used by the API under test (same shape as production).
    public const string AppUser = "clockapp_test_app";
    public const string AppPassword = "Test-App-Passw0rd-9x!";

    private readonly string? _connectionString;
    private readonly string? _appConnectionString;

    public DatabaseFixture()
    {
        if (string.IsNullOrWhiteSpace(ServerConnection)) return;

        var builder = new SqlConnectionStringBuilder(ServerConnection)
        {
            InitialCatalog = $"ClockApp_Test_{Guid.NewGuid():N}",
        };
        _connectionString = builder.ConnectionString;
        _appConnectionString = new SqlConnectionStringBuilder(_connectionString) { UserID = AppUser, Password = AppPassword }.ConnectionString;
        using var db = CreateContext();
        db.Database.Migrate();
        AppUserProvisioner.EnsureAsync(_connectionString, _appConnectionString).GetAwaiter().GetResult();
    }

    /// <summary>Connection string of the least-privilege user. API integration tests use it, so they run with real permissions.</summary>
    public string AppConnectionString => _appConnectionString ?? throw new InvalidOperationException("No test database configured.");

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("No test database configured.");

    public AttendanceDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlServer(ConnectionString).Options);

    public void Dispose()
    {
        if (_connectionString is null) return;
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }
}
