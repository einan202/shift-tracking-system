using ClockApp.Api.Data;
using ClockApp.Attendance.Entities;
using Microsoft.Data.SqlClient;

namespace ClockApp.Tests.Data;

/// <summary>The application's database user is least-privilege: no DDL, no deletes, corrections are append-only.</summary>
public sealed class AppUserPermissionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _db;

    public AppUserPermissionTests(DatabaseFixture db) => _db = db;

    private async Task<SqlConnection> AppConnection()
    {
        var c = new SqlConnection(_db.AppConnectionString);
        await c.OpenAsync();
        return c;
    }

    private static async Task<int> Exec(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>An employee with one punch, created with the privileged connection.</summary>
    private async Task<(int UserId, long RecordId)> SeedAsync()
    {
        await using var db = _db.CreateContext();
        var u = new User { Email = $"{Guid.NewGuid():N}@test.local", FullName = "Perm", Role = UserRole.Employee };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        var r = new AttendanceRecord { UserId = u.Id, ClockInUtc = DateTimeOffset.Parse("2026-02-02T08:00:00Z"), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "test", IsOpen = true };
        db.AttendanceRecords.Add(r);
        await db.SaveChangesAsync();
        return (u.Id, r.Id);
    }

    private static string InsertCorrection(int userId, int adminId) => $"""
        INSERT INTO AttendanceCorrections (Kind, UserId, WorkDate, CorrectedClockInUtc, CorrectedClockOutUtc, Reason, ReasonCode, CreatedByUserId, CreatedAtUtc)
        VALUES ('Add', {userId}, '2026-02-02', '2026-02-02T08:00:00Z', '2026-02-02T09:00:00Z', 'permission test', 'Other', {adminId}, '2026-02-03T08:00:00Z')
        """;

    [RequiresDbFact]
    public async Task App_user_can_do_what_the_application_needs()
    {
        var (userId, recordId) = await SeedAsync();
        await using var c = await AppConnection();

        Assert.Equal(1, await Exec(c, $"UPDATE AttendanceRecords SET IsOpen = 0, ClockOutUtc = '2026-02-02T16:00:00Z' WHERE Id = {recordId}")); // clock-out
        Assert.Equal(1, await Exec(c, InsertCorrection(userId, userId)));                                                                          // correction
        Assert.Equal(1, await Exec(c, $"UPDATE Users SET IsActive = 0 WHERE Id = {userId}"));                                                       // deactivate
        Assert.Equal(1, await Exec(c, "UPDATE ShiftSettings SET DurationMinutes = 480 WHERE Id = 1"));                                              // shift duration
    }

    [RequiresDbFact]
    public async Task Schedule_versions_are_append_only_for_the_app_user()
    {
        var (userId, _) = await SeedAsync();
        await using var c = await AppConnection();
        const string cols = "(UserId, EffectiveFrom, ExpectedMinutesMon, ExpectedMinutesTue, ExpectedMinutesWed, ExpectedMinutesThu, ExpectedMinutesFri, ExpectedMinutesSat, ExpectedMinutesSun, AllowsOvernightShifts, CreatedByUserId, CreatedAtUtc)";

        Assert.Equal(1, await Exec(c, $"INSERT INTO EmployeeScheduleVersions {cols} VALUES ({userId}, '2026-06-01', 510, 510, 510, 510, 360, 0, 0, 0, {userId}, '2026-05-20T08:00:00Z')"));

        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"UPDATE EmployeeScheduleVersions SET ExpectedMinutesMon = 1 WHERE UserId = {userId}"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"DELETE FROM EmployeeScheduleVersions WHERE UserId = {userId}"));
    }

    [RequiresDbFact]
    public async Task Corrections_are_append_only_for_the_app_user()
    {
        var (userId, _) = await SeedAsync();
        await using var c = await AppConnection();
        await Exec(c, InsertCorrection(userId, userId));

        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"UPDATE AttendanceCorrections SET Reason = 'tampered reason' WHERE UserId = {userId}"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"DELETE FROM AttendanceCorrections WHERE UserId = {userId}"));
    }

    [RequiresDbFact]
    public async Task App_user_cannot_delete_punches_or_users()
    {
        var (userId, recordId) = await SeedAsync();
        await using var c = await AppConnection();

        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"DELETE FROM AttendanceRecords WHERE Id = {recordId}"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, $"DELETE FROM Users WHERE Id = {userId}"));
    }

    [RequiresDbFact]
    public async Task App_user_cannot_change_the_schema_or_read_the_migration_history()
    {
        await using var c = await AppConnection();

        await Assert.ThrowsAsync<SqlException>(() => Exec(c, "CREATE TABLE dbo.Evil (Id int)"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, "ALTER TABLE dbo.Users ADD Evil int"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, "DROP TABLE dbo.AttendanceCorrections"));
        await Assert.ThrowsAsync<SqlException>(() => Exec(c, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory"));
    }

    [RequiresDbFact]
    public async Task Provisioning_is_idempotent_and_syncs_a_changed_password()
    {
        // Running it again (a restart) changes nothing.
        await AppUserProvisioner.EnsureAsync(_db.ConnectionString, _db.AppConnectionString);
        await using (await AppConnection()) { }

        // A different password for a NEW user is created, and re-running with a rotated password updates the login.
        var user = $"rot_{Guid.NewGuid():N}"[..20];
        string Cs(string pw) => new SqlConnectionStringBuilder(_db.ConnectionString) { UserID = user, Password = pw }.ConnectionString;
        await AppUserProvisioner.EnsureAsync(_db.ConnectionString, Cs("First-Passw0rd-1!"));
        await AppUserProvisioner.EnsureAsync(_db.ConnectionString, Cs("Second-Passw0rd-2!"));

        await using (var c = new SqlConnection(Cs("Second-Passw0rd-2!"))) await c.OpenAsync();   // the rotated password works
        SqlConnection.ClearAllPools();                                                         // the login must not stay "in use"
        await using var cleanup = new SqlConnection(_db.ConnectionString);
        await cleanup.OpenAsync();
        await Exec(cleanup, $"DROP USER [{user}]; DROP LOGIN [{user}];");
    }

    [Theory]
    [InlineData("x]; DROP TABLE Users;--")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public async Task Unsafe_user_names_are_rejected_before_any_sql_runs(string name)
    {
        var cs = new SqlConnectionStringBuilder { DataSource = "unused", UserID = name, Password = "Whatever-Passw0rd-1!" }.ConnectionString;

        await Assert.ThrowsAsync<ArgumentException>(() => AppUserProvisioner.EnsureAsync("Server=unused", cs));
    }
}
