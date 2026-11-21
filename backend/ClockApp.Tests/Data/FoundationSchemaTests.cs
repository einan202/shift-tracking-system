using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

/// <summary>Iteration 1.1: schema foundation (Void kind, targets, reason codes, schedule versions) and the effective model for voids.</summary>
public sealed class FoundationSchemaTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-02-02T08:00:00Z");

    private async Task<(AttendanceDbContext Db, User Employee, User Admin, AttendanceRecord Punch)> Seed()
    {
        var db = fixture.CreateContext();
        var e = new User { Email = $"{Guid.NewGuid():N}@t.local", FullName = "E", Role = UserRole.Employee };
        var a = new User { Email = $"{Guid.NewGuid():N}@t.local", FullName = "A", Role = UserRole.Admin };
        db.Users.AddRange(e, a);
        await db.SaveChangesAsync();
        var punch = new AttendanceRecord { UserId = e.Id, ClockInUtc = T, ClockOutUtc = T.AddHours(8), WorkDate = new DateOnly(2026, 2, 2), TimeProvider = "test" };
        db.AttendanceRecords.Add(punch);
        await db.SaveChangesAsync();
        return (db, e, a, punch);
    }

    private static AttendanceCorrection Row(User e, User a, CorrectionKind kind, long? recordId = null, long? targetId = null,
        DateTimeOffset? @in = null, DateTimeOffset? @out = null, string code = ReasonCodes.Other) => new()
    {
        Kind = kind, AttendanceRecordId = recordId, TargetCorrectionId = targetId, UserId = e.Id, WorkDate = new DateOnly(2026, 2, 2),
        CorrectedClockInUtc = @in, CorrectedClockOutUtc = @out, Reason = "foundation test", ReasonCode = code,
        CreatedByUserId = a.Id, CreatedAtUtc = T.AddDays(3),
    };

    [RequiresDbFact]
    public async Task Void_and_target_combinations_follow_the_kind_rules()
    {
        var (db, e, a, punch) = await Seed();
        await using var _ = db;

        // Valid: Void of a punch, Add (no target).
        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, recordId: punch.Id));
        var add = Row(e, a, CorrectionKind.Add, @in: T.AddDays(1), @out: T.AddDays(1).AddHours(2));
        db.AttendanceCorrections.Add(add);
        await db.SaveChangesAsync();

        // Valid: Void of a manually added interval (TargetCorrectionId).
        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, targetId: add.Id));
        await db.SaveChangesAsync();

        // Invalid: Void with no target, with both targets, and Add with a target.
        foreach (var bad in new[]
        {
            Row(e, a, CorrectionKind.Void),
            Row(e, a, CorrectionKind.Void, recordId: punch.Id, targetId: add.Id),
            Row(e, a, CorrectionKind.Add, targetId: add.Id, @in: T, @out: T.AddHours(1)),
        })
        {
            db.ChangeTracker.Clear();
            db.AttendanceCorrections.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [RequiresDbFact]
    public async Task Reason_code_must_be_one_of_the_standard_codes()
    {
        var (db, e, a, punch) = await Seed();
        await using var _ = db;

        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, recordId: punch.Id, code: ReasonCodes.ForgotClockOut));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, recordId: punch.Id, code: "Whatever"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [RequiresDbFact]
    public async Task Schedule_versions_need_valid_minutes_and_at_least_one_working_day()
    {
        var (db, e, a, _) = await Seed();
        await using var _db = db;
        EmployeeScheduleVersion V(short mon, short sun = 0) => new()
        {
            UserId = e.Id, EffectiveFrom = new DateOnly(2026, 6, 1), ExpectedMinutesMon = mon, ExpectedMinutesSun = sun,
            CreatedByUserId = a.Id, CreatedAtUtc = T,
        };

        db.EmployeeScheduleVersions.Add(V(510));
        await db.SaveChangesAsync();

        foreach (var bad in new[] { V(0), V(1441), V(-1) })   // no working day, over 24 h, negative
        {
            db.ChangeTracker.Clear();
            db.EmployeeScheduleVersions.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [RequiresDbFact]
    public async Task Pay_type_defaults_to_monthly_and_rejects_unknown_values()
    {
        await using var db = fixture.CreateContext();
        var u = new User { Email = $"{Guid.NewGuid():N}@t.local", FullName = "P", Role = UserRole.Employee };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        Assert.Equal(PayType.Monthly, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == u.Id)).PayType);

        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE Users SET PayType = 'Weekly' WHERE Id = {u.Id}"));
    }

    [RequiresDbFact]
    public async Task Voided_punch_and_voided_manual_entry_leave_every_calculation_but_stay_in_history()
    {
        var (db, e, a, punch) = await Seed();
        await using var _ = db;
        var add = Row(e, a, CorrectionKind.Add, @in: T.AddDays(1), @out: T.AddDays(1).AddHours(2));
        db.AttendanceCorrections.Add(add);
        await db.SaveChangesAsync();
        Assert.Equal(2, (await EffectiveAttendance.LoadAsync(db, e.Id)).Count);   // punch + manual entry

        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, recordId: punch.Id));
        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Void, targetId: add.Id));
        await db.SaveChangesAsync();

        Assert.Empty(await EffectiveAttendance.LoadAsync(db, e.Id));                        // not counted anywhere
        var history = await EffectiveAttendance.LoadAsync(db, e.Id, includeVoided: true);
        Assert.Equal(2, history.Count);
        Assert.All(history, r => Assert.True(r.IsVoided));
        // The original punch row is untouched.
        var original = await db.AttendanceRecords.AsNoTracking().SingleAsync(r => r.Id == punch.Id);
        Assert.Equal(T.AddHours(8), original.ClockOutUtc);
    }

    [RequiresDbFact]
    public async Task A_manual_entry_can_be_adjusted_through_its_add_correction()
    {
        var (db, e, a, _) = await Seed();
        await using var _db = db;
        var add = Row(e, a, CorrectionKind.Add, @in: T.AddDays(1), @out: T.AddDays(1).AddHours(2));
        db.AttendanceCorrections.Add(add);
        await db.SaveChangesAsync();

        db.AttendanceCorrections.Add(Row(e, a, CorrectionKind.Adjust, targetId: add.Id, @in: T.AddDays(1), @out: T.AddDays(1).AddHours(3)));
        await db.SaveChangesAsync();

        var manual = (await EffectiveAttendance.LoadAsync(db, e.Id)).Single(r => r.IsManual);
        Assert.Equal(T.AddDays(1).AddHours(3), manual.Out);
        Assert.True(manual.IsAdjusted);
    }

    [Fact]
    public async Task Month_state_reader_seam_is_always_open_in_milestone_1()
    {
        Assert.Equal(MonthState.Open, await new AlwaysOpenMonthState().GetStateAsync(1, 2026, 2));
    }
}
