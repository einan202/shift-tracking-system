using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ClockApp.Api.Data;

/// <summary>
/// Creates (idempotently) the least-privilege SQL login/user the running application connects as, so the app
/// never needs `sa` or any schema-changing rights. Run with the PRIVILEGED connection (the same one that applies
/// migrations), after the migrations have created the tables.
///
/// Rights: read/insert/update where the application needs them; no DELETE anywhere; AttendanceCorrections is
/// append-only (INSERT and SELECT only, UPDATE and DELETE explicitly denied); no DDL; no access to the
/// migrations history table.
/// </summary>
public static partial class AppUserProvisioner
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex SafeName();

    public static async Task EnsureAsync(string privilegedConnectionString, string appConnectionString, ILogger? log = null, CancellationToken ct = default)
    {
        var app = new SqlConnectionStringBuilder(appConnectionString);
        var user = app.UserID;
        // The name is spliced into DDL (identifiers cannot be parameters), so only plain identifiers are accepted.
        if (!SafeName().IsMatch(user)) throw new ArgumentException("The application database user name must be a simple identifier.", nameof(appConnectionString));
        if (string.IsNullOrEmpty(app.Password)) throw new ArgumentException("The application database user needs a password.", nameof(appConnectionString));
        var password = app.Password.Replace("'", "''");

        // 1) Server-level login, created from master. Serialised with an app lock; app locks are scoped to the
        //    CURRENT DATABASE, so the lock must be taken in master (shared) for parallel starters/tests not to race.
        var masterCs = new SqlConnectionStringBuilder(privilegedConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterCs);
        await master.OpenAsync(ct);
        await Run(master, $"""
            BEGIN TRAN;
            EXEC sp_getapplock @Resource = N'clockapp-provision-login', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{user}')
                CREATE LOGIN [{user}] WITH PASSWORD = N'{password}', CHECK_POLICY = ON;
            COMMIT;
            """, ct);

        // Keep the login's password in sync with the configured one (supports rotation) without touching it when it already matches.
        if (!await CanLogin(app))
        {
            await Run(master, $"ALTER LOGIN [{user}] WITH PASSWORD = N'{password}';", ct);
            log?.LogInformation("Application database login password was synchronised with the configuration.");
        }

        // 2) Database user and permissions in the application database (explicit DENYs state the intent and win over any broader grant).
        await using var conn = new SqlConnection(privilegedConnectionString);
        await conn.OpenAsync(ct);
        await Run(conn, $"""
            IF DATABASE_PRINCIPAL_ID(N'{user}') IS NULL CREATE USER [{user}] FOR LOGIN [{user}];

            GRANT SELECT, INSERT, UPDATE ON dbo.Users TO [{user}];
            DENY  DELETE                 ON dbo.Users TO [{user}];

            GRANT SELECT, INSERT, UPDATE ON dbo.AttendanceRecords TO [{user}];
            DENY  DELETE                 ON dbo.AttendanceRecords TO [{user}];

            GRANT SELECT, INSERT         ON dbo.AttendanceCorrections TO [{user}];
            DENY  UPDATE, DELETE         ON dbo.AttendanceCorrections TO [{user}];

            GRANT SELECT, UPDATE         ON dbo.ShiftSettings TO [{user}];
            DENY  INSERT, DELETE         ON dbo.ShiftSettings TO [{user}];
            """, ct);
        log?.LogInformation("Application database user {User} is provisioned with least-privilege permissions.", user);
    }

    private static async Task Run(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>True when the login's password already matches (probed against master: the db user may not exist yet).</summary>
    private static async Task<bool> CanLogin(SqlConnectionStringBuilder app)
    {
        var probe = new SqlConnectionStringBuilder(app.ConnectionString) { InitialCatalog = "master", Pooling = false };
        try
        {
            await using var c = new SqlConnection(probe.ConnectionString);
            await c.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
