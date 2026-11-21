using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Attendance;

public static class MonthlyReportEndpoint
{
    public static void MapMonthlyReportEndpoint(this IEndpointRouteBuilder app)
    {
        // Own data only: the user id comes from the signed token, never from the request.
        app.MapGet("/api/attendance/me/month", async (
            int? year, int? month, ClaimsPrincipal user, AttendanceDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            // Default month = current Zurich month. The server clock is allowed here: nothing is recorded.
            var today = ZurichTime.LocalDateOf(clock.GetUtcNow());
            var y = year ?? today.Year;
            var m = month ?? today.Month;
            if (y is < 2000 or > 2100 || m is < 1 or > 12)
                return Results.BadRequest(new { error = "Year must be 2000-2100 and month 1-12." });

            var userId = int.Parse(user.FindFirstValue(TokenService.SubjectClaim)!);
            var first = new DateOnly(y, m, 1);
            var records = await EffectiveAttendance.LoadAsync(db, userId, first, first.AddMonths(1).AddDays(-1), ct);
            var defaultMinutes = (await db.ShiftSettings.AsNoTracking().SingleAsync(ct)).DurationMinutes;

            return Results.Ok(MonthlyReport.Build(records, defaultMinutes, y, m));
        }).RequireAuthorization(Policies.Employee);

        // The new timesheet: every calendar day with ONE consolidated effective shift (first clock-in, last clock-out,
        // corrections applied), Expected from the schedule version applicable to each date, Worked and Balance only
        // for finished days. Employees get only per-field "manual" flags: no punches, reasons, edit times or history.
        app.MapGet("/api/attendance/me/timesheet", async (
            int? year, int? month, ClaimsPrincipal user, AttendanceDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = ZurichTime.LocalDateOf(clock.GetUtcNow());   // server clock: display only, nothing is recorded
            var y = year ?? today.Year;
            var m = month ?? today.Month;
            if (y is < 2000 or > 2100 || m is < 1 or > 12)
                return Results.BadRequest(new { error = "Year must be 2000-2100 and month 1-12." });

            var userId = int.Parse(user.FindFirstValue(TokenService.SubjectClaim)!);
            var start = (await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmploymentStartDate).SingleAsync(ct));
            var versions = await db.EmployeeScheduleVersions.AsNoTracking().Where(v => v.UserId == userId).ToListAsync(ct);
            var first = new DateOnly(y, m, 1);
            var effective = await EffectiveAttendance.LoadAsync(db, userId, first, first.AddMonths(1).AddDays(-1), ct);
            var sheet = MonthSheet.Build(y, m, effective, versions, start, today);

            return Results.Ok(new
            {
                year = y, month = m, zurichToday = today,
                days = sheet.Days.Select(d => new
                {
                    date = d.Date, weekday = d.Weekday.ToString(), clockInUtc = d.ClockIn, clockOutUtc = d.ClockOut,
                    clockInIsManual = d.ClockInIsManual, clockOutIsManual = d.ClockOutIsManual,
                    inProgress = d.InProgress, needsCorrection = d.NeedsCorrection,
                    workedMinutes = d.WorkedMinutes, expectedMinutes = d.ExpectedMinutes, balanceMinutes = d.BalanceMinutes,
                    isToday = d.IsToday, isFuture = d.IsFuture,
                }),
                monthToDate = new { workedMinutes = sheet.MonthToDate.WorkedMinutes, expectedMinutes = sheet.MonthToDate.ExpectedMinutes, balanceMinutes = sheet.MonthToDate.BalanceMinutes },
                wholeMonth = new { expectedMinutes = sheet.MonthToDate.WholeMonthExpectedMinutes },
            });
        }).RequireAuthorization(Policies.Employee);
    }
}
