using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Admin;

public sealed record ShiftSettingsRequest(int? DurationMinutes, int[]? Minutes = null);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings/shift").RequireAuthorization(Policies.Admin);

        // The company default is a DURATION per weekday (no start times; 0 = not a working day). It only prefills new employees.
        object Dto(ShiftSettings x) => new { durationMinutes = x.DurationMinutes, minutes = x.Week };

        group.MapGet("", async (AttendanceDbContext db) => Results.Ok(Dto(await db.ShiftSettings.AsNoTracking().SingleAsync())));

        group.MapPut("", async (ShiftSettingsRequest req, ClaimsPrincipal user, AttendanceDbContext db) =>
        {
            // Preferred body: minutes[7] (Monday..Sunday). The legacy single durationMinutes still works and means Monday-Friday.
            int[] week;
            if (req.Minutes is not null)
            {
                week = req.Minutes;
                if (week.Length != 7 || week.Any(m => m is < 0 or > 1440) || week.All(m => m == 0))
                    return Results.BadRequest(new { error = "Give seven durations between 0 and 1440 minutes (Monday to Sunday) with at least one working day." });
            }
            else
            {
                // Mirrors the DB check constraint (1..1440) so the caller gets a clear 400 instead of a DB error.
                if (req.DurationMinutes is not (>= 1 and <= 1440))
                    return Results.BadRequest(new { error = "Duration must be between 1 and 1440 minutes." });
                var d = req.DurationMinutes.Value;
                week = [d, d, d, d, d, 0, 0];
            }

            var settings = await db.ShiftSettings.SingleAsync();
            settings.SetWeek(week);
            settings.UpdatedByUserId = int.Parse(user.FindFirstValue(TokenService.SubjectClaim)!);
            await db.SaveChangesAsync();
            return Results.Ok(Dto(settings));
        });
    }
}
