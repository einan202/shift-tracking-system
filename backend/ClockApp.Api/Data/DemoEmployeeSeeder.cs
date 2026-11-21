using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Data;

/// <summary>
/// Adds a few example employees on the very first start so the Employees screen is not empty. They have different
/// working weeks, no password (the admin issues a one-time link when needed) and NO attendance: attendance times must
/// come from the external time API, so nothing is invented here.
/// </summary>
public static class DemoEmployeeSeeder
{
    public static readonly (string Name, string Email, int[] Minutes, bool Overnight)[] Examples =
    [
        ("Anna Meier",  "anna.meier@example.com",  [510, 510, 510, 510, 510, 0, 0], false),   // full time, Mon-Fri 8h30
        ("Marco Rossi", "marco.rossi@example.com", [510, 510, 510, 510, 360, 0, 0], false),   // Mon-Thu 8h30, Friday 6h
        ("Lea Keller",  "lea.keller@example.com",  [360, 0, 360, 0, 360, 0, 0],     false),   // part time: Mon, Wed, Fri 6h
        ("Noah Weber",  "noah.weber@example.com",  [480, 480, 480, 480, 480, 0, 0], true),    // night shifts allowed
    ];

    /// <summary>Creates the examples only when there are no employees at all. Returns how many were created.</summary>
    public static async Task<int> SeedAsync(AttendanceDbContext db, int createdByUserId, DateTimeOffset nowUtc)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Employee)) return 0;   // never mix examples into real data

        // Employment starts on the first day of the current Zurich month (display/audit only; nothing is recorded as attendance).
        var today = ZurichTime.LocalDateOf(nowUtc);
        var start = new DateOnly(today.Year, today.Month, 1);
        var created = 0;

        foreach (var (name, email, minutes, overnight) in Examples)
        {
            var user = new User { Email = email, FullName = name, Role = UserRole.Employee, IsActive = true, EmploymentStartDate = start };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var version = new EmployeeScheduleVersion
            {
                UserId = user.Id, EffectiveFrom = start, AllowsOvernightShifts = overnight,
                CreatedByUserId = createdByUserId, CreatedAtUtc = nowUtc,
            };
            ScheduleResolver.Apply(version, minutes);
            db.EmployeeScheduleVersions.Add(version);
            await db.SaveChangesAsync();
            created++;
        }
        return created;
    }
}
