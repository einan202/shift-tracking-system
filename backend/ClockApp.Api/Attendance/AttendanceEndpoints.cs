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
            Guard(log, async () => ToResult(await svc.ClockInAsync(UserId(user), ct), conflict: "You are already clocked in.")));

        group.MapPost("/clock-out", (ClaimsPrincipal user, AttendanceService svc, ILogger<AttendanceService> log, CancellationToken ct) =>
            Guard(log, async () => ToResult(await svc.ClockOutAsync(UserId(user), ct), conflict: "You are not clocked in.")));

        // Source of truth for the UI after a failure, timeout or page reload. Does not call the time API.
        group.MapGet("/status", async (ClaimsPrincipal user, AttendanceService svc, CancellationToken ct) =>
        {
            var record = await svc.GetCurrentOrLastAsync(UserId(user), ct);
            return Results.Ok(new { clockedIn = record?.IsOpen ?? false, record = record is null ? null : Dto(record) });
        });
    }

    private static int UserId(ClaimsPrincipal p) => int.Parse(p.FindFirstValue(TokenService.SubjectClaim)!);

    private static object Dto(AttendanceRecord r) => new
    {
        id = r.Id,
        clockInUtc = r.ClockInUtc,
        clockOutUtc = r.ClockOutUtc,
        workDate = r.WorkDate,
        provider = r.TimeProvider,
    };

    private static IResult ToResult(ClockResult result, string conflict) => result.Outcome switch
    {
        ClockOutcome.Recorded => Results.Ok(new { clockedIn = result.Record!.IsOpen, record = Dto(result.Record) }),
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
