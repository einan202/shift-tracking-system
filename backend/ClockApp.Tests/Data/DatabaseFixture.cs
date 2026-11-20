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

    private readonly string? _connectionString;

    public DatabaseFixture()
    {
        if (string.IsNullOrWhiteSpace(ServerConnection)) return;

        var builder = new SqlConnectionStringBuilder(ServerConnection)
        {
            InitialCatalog = $"ClockApp_Test_{Guid.NewGuid():N}",
        };
        _connectionString = builder.ConnectionString;
        using var db = CreateContext();
        db.Database.Migrate();
    }

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
