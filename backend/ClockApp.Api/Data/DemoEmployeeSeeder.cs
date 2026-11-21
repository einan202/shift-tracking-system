using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Data;

/// <summary>
/// Adds a few example employees on the very first start so the Employees screen is not empty. They have different
/// working weeks (all including Sunday) and no password (the admin issues a one-time link when needed).
/// Each also gets example shifts over the last two weeks. Clock times must come from the external time API, so
/// no punches are invented: every example shift is a manual entry (an "Add" correction with the reason "Example data"),
/// and the example changes and deletions are corrections chained onto those entries, exactly like the app's own edits.
/// Each employee's special example falls on a different day.
/// </summary>
public static class DemoEmployeeSeeder
{
    public static readonly (string Name, string Email, int[] Minutes, bool Overnight)[] Examples =
    [
        ("Anna Meier",  "anna.meier@example.com",  [510, 510, 510, 510, 510, 0, 510], false),   // full time, Mon-Fri and Sunday 8h30
        ("Marco Rossi", "marco.rossi@example.com", [510, 510, 510, 510, 360, 0, 510], false),   // Mon-Thu and Sunday 8h30, Friday 6h
        ("Lea Keller",  "lea.keller@example.com",  [360, 0, 360, 0, 360, 0, 360],   false),   // part time: Mon, Wed, Fri and Sunday 6h
        ("Noah Weber",  "noah.weber@example.com",  [480, 480, 480, 480, 480, 0, 480], true),    // night shifts allowed, Mon-Fri and Sunday 8h
    ];

    public const int HistoryDays = 14;
    public const string ExampleReason = "Example data";

    /// <summary>Creates the examples only when there are no employees at all. Returns how many employees were created.</summary>
    public static async Task<int> SeedAsync(AttendanceDbContext db, int createdByUserId, DateTimeOffset nowUtc)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Employee)) return 0;   // never mix examples into real data

        // Employment starts two weeks ago (Zurich date, display/audit only; nothing here is a recorded punch).
        var today = ZurichTime.LocalDateOf(nowUtc);
        var start = today.AddDays(-HistoryDays);
        var specialDays = new HashSet<DateOnly>();   // every employee's special example gets its own day
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

            await SeedShiftsAsync(db, user, name, minutes, overnight, start, today, nowUtc, specialDays);
            created++;
        }
        return created;
    }

    private static int WeekdayIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;   // Monday = 0 ... Sunday = 6

    private static DateTimeOffset At(DateOnly day, int hour, int minute) =>
        ZurichTime.LocalToUtc(day.ToDateTime(new TimeOnly(hour, minute)));

    private static DateOnly? Pick(List<DateOnly> days, int preferred, HashSet<DateOnly> used)
    {
        for (var k = 0; k < days.Count; k++)
        {
            var d = days[(preferred + k) % days.Count];
            if (used.Add(d)) return d;
        }
        return null;
    }

    private static async Task SeedShiftsAsync(AttendanceDbContext db, User user, string name, int[] minutes, bool overnight,
        DateOnly start, DateOnly today, DateTimeOffset nowUtc, HashSet<DateOnly> specialDays)
    {
        // Past working days only (never today or the future); a night shift must also have ended already.
        var days = new List<DateOnly>();
        for (var d = start; d < today; d = d.AddDays(1))
        {
            var expected = minutes[WeekdayIndex(d)];
            if (expected == 0) continue;
            var (_, shiftOut) = ShiftTimes(d, expected, overnight);
            if (shiftOut <= nowUtc) days.Add(d);
        }
        if (days.Count == 0) return;

        Func<Guid> newBatch = Guid.NewGuid;   // one batch per employee action
        AttendanceCorrection Row(CorrectionKind kind, DateOnly day, DateTimeOffset? cin, DateTimeOffset? cout, string code, string reason,
            long? target = null, DateTimeOffset? pin = null, DateTimeOffset? pout = null) => new()
        {
            Kind = kind, TargetCorrectionId = target, BatchId = newBatch(), ReasonCode = code, UserId = user.Id, WorkDate = day,
            CorrectedClockInUtc = cin, CorrectedClockOutUtc = cout, PreviousClockInUtc = pin, PreviousClockOutUtc = pout,
            Reason = reason, CreatedByUserId = user.Id, CreatedAtUtc = nowUtc,
        };
        async Task<long> AddShift(DateOnly day, DateTimeOffset cin, DateTimeOffset cout, string reason = ExampleReason)
        {
            var add = Row(CorrectionKind.Add, day, cin, cout, ReasonCodes.Other, reason);
            db.AttendanceCorrections.Add(add);
            await db.SaveChangesAsync();
            return add.Id;
        }

        // The special example of each employee sits on its own day (never the same day as another employee's).
        DateOnly? missing = null, extra = null, changed = null, deleted = null;
        switch (name)
        {
            case "Anna Meier": missing = Pick(days, 1, specialDays); extra = Pick(days, days.Count / 2, specialDays); break;
            case "Marco Rossi": changed = Pick(days, 2, specialDays); break;
            case "Lea Keller": deleted = Pick(days, 3, specialDays); break;
            case "Noah Weber": extra = Pick(days, 1, specialDays); break;
        }

        foreach (var day in days)
        {
            if (day == missing) continue;                                   // a working day without any shift
            var (cin, cout) = ShiftTimes(day, minutes[WeekdayIndex(day)], overnight);

            if (day == extra)                                                // extra hours
            {
                var longOut = overnight ? cin.AddHours(10) : At(day, 19, 30);
                await AddShift(day, overnight ? cin : At(day, 8, 0), longOut, "Example data: worked extra hours on a project");
            }
            else if (day == changed)                                         // a manual change: the clock-out was adjusted
            {
                var early = cout.AddHours(-2);                               // the clock-out that was first entered
                var id = await AddShift(day, cin, early);
                db.AttendanceCorrections.Add(Row(CorrectionKind.Adjust, day, cin, cout, ReasonCodes.ForgotClockOut,
                    ShiftEditService.Labels[ReasonCodes.ForgotClockOut], id, cin, early));
                await db.SaveChangesAsync();
            }
            else if (day == deleted)                                         // a shift that was deleted again
            {
                var id = await AddShift(day, cin, cout);
                db.AttendanceCorrections.Add(Row(CorrectionKind.Void, day, null, null, ReasonCodes.DuplicateOrAccidental,
                    ShiftEditService.Labels[ReasonCodes.DuplicateOrAccidental], id, cin, cout));
                await db.SaveChangesAsync();
            }
            else await AddShift(day, cin, cout);                             // a normal day: exactly the expected hours
        }
    }

    /// <summary>Normal shift of a day: 08:30 for the expected duration, or 22:00 for employees who may work nights.</summary>
    private static (DateTimeOffset In, DateTimeOffset Out) ShiftTimes(DateOnly day, int expectedMinutes, bool overnight)
    {
        var cin = overnight ? At(day, 22, 0) : At(day, 8, 30);
        return (cin, cin.AddMinutes(expectedMinutes));
    }
}
