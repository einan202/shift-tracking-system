using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

public class SchemaConstraintTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));

    private static User NewUser(UserRole role = UserRole.Employee) => new()
    {
        Email = $"{Guid.NewGuid():N}@test.local",
        FullName = "Test User",
        Role = role,
    };

    private static AttendanceRecord Open(int userId, DateTimeOffset? at = null) => new()
    {
        UserId = userId,
        ClockInUtc = (at ?? T0).ToUniversalTime(),
        WorkDate = new DateOnly(2026, 6, 1),
        TimeProvider = "test",
        IsOpen = true,
    };

    private async Task<(AttendanceDbContext Db, User User)> UserInDb(UserRole role = UserRole.Employee)
    {
        var db = fixture.CreateContext();
        var user = NewUser(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (db, user);
    }

    [RequiresDbFact]
    public async Task Migration_seeds_single_default_shift_row()
    {
        await using var db = fixture.CreateContext();
        var shift = await db.ShiftSettings.SingleAsync();
        Assert.Equal(ShiftSettings.SingletonId, shift.Id);
        Assert.Equal(AttendanceDbContext.DefaultShiftMinutes, shift.DurationMinutes);
    }

    [RequiresDbFact]
    public async Task Attendance_tables_have_no_database_default_constraints()
    {
        await using var db = fixture.CreateContext();
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.name + '.' + c.name
            FROM sys.default_constraints dc
            JOIN sys.tables t ON t.object_id = dc.parent_object_id
            JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE t.name IN ('AttendanceRecords', 'AttendanceCorrections')
            """;
        var withDefaults = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) withDefaults.Add(reader.GetString(0));

        Assert.Empty(withDefaults);
    }

    [RequiresDbFact]
    public async Task Email_must_be_unique()
    {
        var (db, user) = await UserInDb();
        await using var _ = db;
        db.Users.Add(new User { Email = user.Email, FullName = "Dup", Role = UserRole.Employee });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [RequiresDbFact]
    public async Task Role_is_restricted_to_known_values()
    {
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Users (Email, FullName, Role) VALUES ('x@test.local', 'X', 'Hacker')"));
    }

    [RequiresDbFact]
    public async Task Second_open_record_for_same_user_is_rejected()
    {
        var (db, user) = await UserInDb();
        await using var _ = db;
        db.AttendanceRecords.Add(Open(user.Id));
        await db.SaveChangesAsync();

        db.AttendanceRecords.Add(Open(user.Id, T0.AddHours(1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [RequiresDbFact]
    public async Task Closed_records_and_other_users_do_not_conflict_with_an_open_record()
    {
        var (db, user) = await UserInDb();
        await using var _ = db;
        var other = NewUser();
        db.Users.Add(other);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = user.Id, ClockInUtc = T0.ToUniversalTime(), ClockOutUtc = T0.AddHours(8).ToUniversalTime(),
            WorkDate = new DateOnly(2026, 6, 1), TimeProvider = "test", IsOpen = false,
        });
        await db.SaveChangesAsync();
        db.AttendanceRecords.Add(Open(user.Id, T0.AddDays(1)));
        db.AttendanceRecords.Add(Open(other.Id));

        await db.SaveChangesAsync(); // must not throw
    }

    [RequiresDbFact]
    public async Task Clock_out_must_be_after_clock_in()
    {
        var (db, user) = await UserInDb();
        await using var _ = db;
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = user.Id, ClockInUtc = T0.ToUniversalTime(), ClockOutUtc = T0.ToUniversalTime(),
            WorkDate = new DateOnly(2026, 6, 1), TimeProvider = "test", IsOpen = false,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [RequiresDbFact]
    public async Task Shift_settings_is_a_single_row_with_valid_duration()
    {
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ShiftSettings (Id, DurationMinutes) VALUES (2, 480)"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE ShiftSettings SET DurationMinutes = 0"));
    }

    [RequiresDbFact]
    public async Task Correction_requires_a_meaningful_reason_and_consistent_times()
    {
        var (db, admin) = await UserInDb(UserRole.Admin);
        await using var _ = db;
        AttendanceCorrection Correction(string reason, DateTimeOffset @in, DateTimeOffset @out) => new()
        {
            Kind = CorrectionKind.Add, UserId = admin.Id, CreatedByUserId = admin.Id,
            WorkDate = new DateOnly(2026, 6, 1), Reason = reason,
            CorrectedClockInUtc = @in.ToUniversalTime(), CorrectedClockOutUtc = @out.ToUniversalTime(),
            CreatedAtUtc = T0.ToUniversalTime(),
        };

        db.AttendanceCorrections.Add(Correction("   ", T0, T0.AddHours(8)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.AttendanceCorrections.Add(Correction("Forgot to clock", T0.AddHours(8), T0));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.AttendanceCorrections.Add(Correction("Forgot to clock out", T0, T0.AddHours(8)));
        await db.SaveChangesAsync(); // valid correction is accepted
    }
}
