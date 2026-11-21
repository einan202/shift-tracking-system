using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;

namespace ClockApp.Api.Attendance;

public static class AttendanceEndpoints
{
    public static void MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Employees only. NONE of these endpoints accepts a body, query value or header that carries a time:
        // the recorded time is fetched server-side from the external API, so a forged timestamp has nowhere to go.
        var group = app.MapGroup("/api/attendance").RequireAuthorization(Policies.Employee);

        group.MapPost("/clock-in", (ClaimsPrincipal user, AttendanceService svc, ILogger<AttendanceService> log, CancellationToken ct) =>
            Guard(log, async () => await ToResult(svc, UserId(user), await svc.ClockInAsync(UserId(user), ct), "You are already clocked in.", ct)));

        group.MapPost("/clock-out", (ClaimsPrincipal user, AttendanceService svc, ILogger<AttendanceService> log, CancellationToken ct) =>
            Guard(log, async () => await ToResult(svc, UserId(user), await svc.ClockOutAsync(UserId(user), ct), "You are not clocked in.", ct)));

        // Source of truth for the UI after a failure, timeout or page reload. Does not call the time API.
        group.MapGet("/status", async (ClaimsPrincipal user, AttendanceService svc, CancellationToken ct) =>
        {
            var record = await svc.GetCurrentOrLastAsync(UserId(user), ct);
            return Results.Ok(new { clockedIn = record?.IsOpen ?? false, record = record is null ? null : Dto(record) });
        });
    }

    private static int UserId(ClaimsPrincipal p) => int.Parse(p.FindFirstValue(TokenService.SubjectClaim)!);

    // Effective values (admin corrections applied). isAdjusted / isManual say when they differ from the punch.
    private static object Dto(EffectiveRecord r) => new
    {
        id = r.RecordId,
        clockInUtc = r.In,
        clockOutUtc = r.Out,
        workDate = r.WorkDate,
        provider = r.Provider ?? "manual",
        isAdjusted = r.IsAdjusted,
        isManual = r.IsManual,
    };

    private static async Task<IResult> ToResult(AttendanceService svc, int userId, ClockResult result, string conflict, CancellationToken ct)
    {
        if (result.Outcome != ClockOutcome.Recorded) return ConflictResult(result, conflict);

        // Report the punch that was just recorded, with any correction already applied.
        var recorded = await svc.GetEffectiveAsync(userId, result.Record!.Id, ct);
        return Results.Ok(new { clockedIn = recorded!.IsOpen, record = Dto(recorded) });
    }

    private static IResult ConflictResult(ClockResult result, string conflict) => result.Outcome switch
    {
        ClockOutcome.ConflictsWithCorrection => Results.Json(
            new { error = $"Your clock-in was corrected to {ZurichTime.ToLocal(result.CorrectedClockIn!.Value):yyyy-MM-dd HH:mm} (Zurich time), which is later than this clock-out. Nothing was recorded; try again in a moment or ask an administrator." },
            statusCode: StatusCodes.Status409Conflict),
        ClockOutcome.NonWorkingDay => Results.Json(
            new { error = "Today is not one of your working days, so you cannot clock in. If you did work, add the shift from your timesheet with a reason." },
            statusCode: StatusCodes.Status409Conflict),
        ClockOutcome.OverlapsExisting => Results.Json(
            new { error = "This overlaps a corrected record of yours. Please contact an administrator." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { error = conflict }, statusCode: StatusCodes.Status409Conflict),
    };

    /// <summary>
    /// Fail closed: if the external time API is unavailable or inconsistent the request ends with 503 and
    /// nothing was written (the service throws before any insert/update). No local clock is ever substituted.
    /// </summary>
    private static async Task<IResult> Guard(ILogger log, Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AttendanceBusyException ex)
        {
            // Another punch/correction of this user held the lock too long: nothing was written, retry is safe.
            log.LogWarning(ex, "Attendance lock busy; punch rejected.");
            return Results.Json(
                new { error = "Your attendance is being updated. Nothing was recorded; please try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable,
                contentType: null)
                .WithRetryAfter(2);
        }
        catch (TimeSourceException ex)
        {
            log.LogWarning(ex, "Time source failure; punch rejected.");
            return Results.Json(
                new { error = "The time service is unavailable. Nothing was recorded; please try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable,
                contentType: null)
                .WithRetryAfter(5);
        }
    }

    private static IResult WithRetryAfter(this IResult inner, int seconds) => new RetryAfterResult(inner, seconds);

    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx)
        {
            ctx.Response.Headers.RetryAfter = seconds.ToString();
            return inner.ExecuteAsync(ctx);
        }
    }
}
