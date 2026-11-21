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
    }
}
