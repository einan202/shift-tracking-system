using ClockApp.Api.Data;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

public class DemoEmployeeSeederTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDbFact]
    public async Task Seeds_the_examples_once_with_their_weeks_and_without_any_attendance()
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
            Assert.Equal(new DateOnly(2026, 11, 1), e.EmploymentStartDate);
        });

        var versions = await db.EmployeeScheduleVersions.AsNoTracking().ToListAsync();
        Assert.Equal(4, versions.Count);
        var anna = versions.Single(v => v.UserId == employees[0].Id);
        var marco = versions.Single(v => v.UserId == employees[1].Id);
        var lea = versions.Single(v => v.UserId == employees[2].Id);
        var noah = versions.Single(v => v.UserId == employees[3].Id);
        Assert.Equal([510, 510, 510, 510, 510, 0, 0], new[] { anna.ExpectedMinutesMon, anna.ExpectedMinutesTue, anna.ExpectedMinutesWed, anna.ExpectedMinutesThu, anna.ExpectedMinutesFri, anna.ExpectedMinutesSat, anna.ExpectedMinutesSun }.Select(x => (int)x));
        Assert.Equal(360, marco.ExpectedMinutesFri);
        Assert.Equal(510, marco.ExpectedMinutesThu);
        Assert.Equal(0, lea.ExpectedMinutesTue);
        Assert.Equal(360, lea.ExpectedMinutesWed);
        Assert.True(noah.AllowsOvernightShifts);
        Assert.False(anna.AllowsOvernightShifts);
        Assert.All(versions, v => Assert.Equal(adminId, v.CreatedByUserId));

        // Clock times must come from the time API, so nothing is invented here.
        Assert.Empty(await db.AttendanceRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AttendanceCorrections.AsNoTracking().ToListAsync());
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
