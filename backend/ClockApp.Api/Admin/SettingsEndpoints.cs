using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Admin;

public sealed record ShiftSettingsRequest(int? DurationMinutes);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings/shift").RequireAuthorization(Policies.Admin);

        // The default shift is a DURATION only (no start time). One row for the whole company.
        group.MapGet("", async (AttendanceDbContext db) =>
            Results.Ok(new { durationMinutes = (await db.ShiftSettings.AsNoTracking().SingleAsync()).DurationMinutes }));

        group.MapPut("", async (ShiftSettingsRequest req, ClaimsPrincipal user, AttendanceDbContext db) =>
        {
            // Mirrors the DB check constraint (1..1440) so the caller gets a clear 400 instead of a DB error.
            if (req.DurationMinutes is not (>= 1 and <= 1440))
                return Results.BadRequest(new { error = "Duration must be between 1 and 1440 minutes." });

            var settings = await db.ShiftSettings.SingleAsync();
            settings.DurationMinutes = req.DurationMinutes.Value;
            settings.UpdatedByUserId = int.Parse(user.FindFirstValue(TokenService.SubjectClaim)!);
            await db.SaveChangesAsync();
            return Results.Ok(new { durationMinutes = settings.DurationMinutes });
        });
    }
}
