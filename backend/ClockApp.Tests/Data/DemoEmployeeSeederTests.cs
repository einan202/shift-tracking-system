using ClockApp.Api.Data;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

public class DemoEmployeeSeederTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDbFact]
    public async Task Seeds_the_examples_once_with_their_weeks_and_manual_example_shifts_on_different_days()
    {
        await using var db = fixture.CreateContext();
        await AdminSeeder.SeedAsync(db, new PasswordHasher<User>(), "demo-admin@test.local", "correct-horse-battery");
        var adminId = (await db.Users.SingleAsync(u => u.Role == UserRole.Admin)).Id;
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

        var first = await DemoEmployeeSeeder.SeedAsync(db, adminId, now);
        var second = await DemoEmployeeSeeder.SeedAsync(db, adminId, now);   // a restart must not add them again

        Assert.Equal(4, first);
        Assert.Equal(0, second);
        var employees = await db.Users.AsNoTracking().Where(u => u.Role == UserRole.Employee).OrderBy(u => u.Id).ToListAsync();
        Assert.Equal(["Anna Meier", "Marco Rossi", "Lea Keller", "Noah Weber"], employees.Select(e => e.FullName));
        Assert.All(employees, e =>
        {
            Assert.True(e.IsActive);
            Assert.Null(e.PasswordHash);                 // nobody can sign in until the admin issues a link
            Assert.Null(e.InviteTokenHash);
            Assert.Equal(new DateOnly(2026, 11, 7), e.EmploymentStartDate);   // two weeks before the (Zurich) day of 'now'
        });

        var versions = await db.EmployeeScheduleVersions.AsNoTracking().ToListAsync();
        Assert.Equal(4, versions.Count);
        var anna = versions.Single(v => v.UserId == employees[0].Id);
        var marco = versions.Single(v => v.UserId == employees[1].Id);
        var lea = versions.Single(v => v.UserId == employees[2].Id);
        var noah = versions.Single(v => v.UserId == employees[3].Id);
        Assert.Equal([510, 510, 510, 510, 510, 0, 510], new[] { anna.ExpectedMinutesMon, anna.ExpectedMinutesTue, anna.ExpectedMinutesWed, anna.ExpectedMinutesThu, anna.ExpectedMinutesFri, anna.ExpectedMinutesSat, anna.ExpectedMinutesSun }.Select(x => (int)x));
        // Every example week includes Sunday and keeps Saturday free.
        Assert.All(new[] { anna, marco, lea, noah }, v => { Assert.True(v.ExpectedMinutesSun > 0); Assert.Equal(0, v.ExpectedMinutesSat); });
        Assert.Equal(510, marco.ExpectedMinutesSun);
        Assert.Equal(360, lea.ExpectedMinutesSun);
        Assert.Equal(480, noah.ExpectedMinutesSun);
        Assert.Equal(360, marco.ExpectedMinutesFri);
        Assert.Equal(510, marco.ExpectedMinutesThu);
        Assert.Equal(0, lea.ExpectedMinutesTue);
        Assert.Equal(360, lea.ExpectedMinutesWed);
        Assert.True(noah.AllowsOvernightShifts);
        Assert.False(anna.AllowsOvernightShifts);
        Assert.All(versions, v => Assert.Equal(adminId, v.CreatedByUserId));

        // Clock times must come from the time API, so no punch is invented: every example shift is a manual entry.
        Assert.Empty(await db.AttendanceRecords.AsNoTracking().ToListAsync());
        var start = new DateOnly(2026, 11, 7);
        var today = new DateOnly(2026, 11, 21);
        var corrections = await db.AttendanceCorrections.AsNoTracking().ToListAsync();
        Assert.NotEmpty(corrections);
        Assert.All(corrections, c =>
        {
            Assert.Equal(c.UserId, c.CreatedByUserId);                 // as if the employee had entered it
            Assert.True(c.WorkDate >= start && c.WorkDate < today);    // past days only, never today or the future
            Assert.True(c.Reason.Trim().Length >= 5);
        });

        async Task<List<EffectiveRecord>> Load(int id, bool voided = false) =>
            await EffectiveAttendance.LoadAsync(db, id, start, today, ct: CancellationToken.None, includeVoided: voided);
        static DateOnly Zurich(DateTimeOffset t) => ZurichTime.LocalDateOf(t);

        // Anna: one missing working day and one extra-long day.
        var annaDays = Enumerable.Range(0, 14).Select(i => start.AddDays(i)).Where(d => new[] { 1, 2, 3, 4, 5, 0 }.Contains((int)d.DayOfWeek)).ToList();
        var annaShifts = await Load(employees[0].Id);
        var annaMissing = annaDays.Except(annaShifts.Select(s => s.WorkDate)).ToList();
        var annaExtra = annaShifts.Where(s => s.Out - s.In > TimeSpan.FromMinutes(510)).ToList();
        Assert.Single(annaMissing);
        Assert.Single(annaExtra);

        // Marco: one shift whose clock-out was adjusted.
        var marcoAdjusted = (await Load(employees[1].Id)).Where(s => s.IsAdjusted).ToList();
        Assert.Single(marcoAdjusted);
        Assert.Contains(corrections, c => c.UserId == employees[1].Id && c.Kind == CorrectionKind.Adjust && c.ReasonCode == ReasonCodes.ForgotClockOut);

        // Lea: one deleted shift (visible to the admin only, not counted).
        var leaVoided = (await Load(employees[2].Id, voided: true)).Where(s => s.IsVoided).ToList();
        Assert.Single(leaVoided);
        Assert.DoesNotContain(await Load(employees[2].Id), s => s.WorkDate == leaVoided[0].WorkDate);

        // Noah: night shifts that end after midnight, one of them extra long.
        var noahShifts = await Load(employees[3].Id);
        Assert.NotEmpty(noahShifts);
        Assert.All(noahShifts, s => { Assert.NotNull(s.Out); Assert.True(Zurich(s.Out!.Value) > s.WorkDate); });
        var noahExtra = noahShifts.Where(s => s.Out - s.In == TimeSpan.FromHours(10)).ToList();
        Assert.Single(noahExtra);

        // Each employee's special example sits on its own day.
        var special = new[] { annaMissing[0], annaExtra[0].WorkDate, marcoAdjusted[0].WorkDate, leaVoided[0].WorkDate, noahExtra[0].WorkDate };
        Assert.Equal(5, special.Distinct().Count());
    }
}

public class DemoEmployeeSeederExistingDataTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDbFact]
    public async Task Adds_nothing_when_any_employee_already_exists()
    {
        await using var db = fixture.CreateContext();
        await AdminSeeder.SeedAsync(db, new PasswordHasher<User>(), "demo-admin2@test.local", "correct-horse-battery");
        var adminId = (await db.Users.SingleAsync(u => u.Role == UserRole.Admin)).Id;
        db.Users.Add(new User { Email = "real.person@company.test", FullName = "Real Person", Role = UserRole.Employee, IsActive = true });
        await db.SaveChangesAsync();

        var created = await DemoEmployeeSeeder.SeedAsync(db, adminId, new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, created);
        Assert.Single(await db.Users.AsNoTracking().Where(u => u.Role == UserRole.Employee).ToListAsync());
        Assert.Empty(await db.EmployeeScheduleVersions.AsNoTracking().ToListAsync());
    }
}
