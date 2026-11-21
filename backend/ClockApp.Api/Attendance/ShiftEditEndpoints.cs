using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Attendance;

public sealed record ShiftEditDto(
    DateOnly Date, DateTime? ClockInLocal, int? ClockInOffsetMinutes, DateTime? ClockOutLocal, int? ClockOutOffsetMinutes,
    string? ReasonCode, string? ReasonText);

/// <summary>
/// The employee fixes their own shifts. Own data only (id from the token). Times are Zurich wall-clock, never sent as
/// instants; the server clock is used only for "not in the future" and audit metadata. Responses show the effective
/// before/after day only: no punches, reasons or history.
/// </summary>
public static class ShiftEditEndpoints
{
    public static void MapShiftEditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attendance").RequireAuthorization(Policies.Employee);

        group.MapGet("reasons", () => ShiftEditService.Labels.Select(kv => new { code = kv.Key, label = kv.Value }));

        group.MapPost("me/shifts/preview", (ShiftEditDto dto, ClaimsPrincipal user, AttendanceDbContext db, ShiftEditService svc, TimeProvider clock, CancellationToken ct) =>
            Run(dto, user, db, svc, clock, commit: false, ct));
        group.MapPost("me/shifts", (ShiftEditDto dto, ClaimsPrincipal user, AttendanceDbContext db, ShiftEditService svc, TimeProvider clock, CancellationToken ct) =>
            Run(dto, user, db, svc, clock, commit: true, ct));
    }

    private static async Task<IResult> Run(
        ShiftEditDto dto, ClaimsPrincipal user, AttendanceDbContext db, ShiftEditService svc, TimeProvider clock, bool commit, CancellationToken ct)
    {
        DateTimeOffset? clockIn, clockOut;
        try { clockIn = ToUtc(dto.ClockInLocal, dto.ClockInOffsetMinutes); clockOut = ToUtc(dto.ClockOutLocal, dto.ClockOutOffsetMinutes); }
        catch (InvalidLocalTimeException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }

        var userId = int.Parse(user.FindFirstValue(TokenService.SubjectClaim)!);
        var req = new ShiftEditRequest(userId, dto.Date, clockIn, clockOut, dto.ReasonCode ?? "", dto.ReasonText);
        var now = clock.GetUtcNow();
        try
        {
            var plan = commit ? await svc.CommitAsync(req, now, ct) : await svc.PreviewAsync(req, now, ct);

            var start = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmploymentStartDate).SingleAsync(ct);
            var versions = await db.EmployeeScheduleVersions.AsNoTracking().Where(v => v.UserId == userId).ToListAsync(ct);
            var today = ZurichTime.LocalDateOf(now);
            object Side(IReadOnlyList<EffectiveRecord> recs)
            {
                var m = dto.Date.Month;
                var sheet = MonthSheet.Build(dto.Date.Year, m, recs.Where(r => r.WorkDate.Year == dto.Date.Year && r.WorkDate.Month == m).ToList(), versions, start, today);
                var d = sheet.Days.Single(x => x.Date == dto.Date);
                return new
                {
                    day = new
                    {
                        date = d.Date, clockInUtc = d.ClockIn, clockOutUtc = d.ClockOut,
                        clockInIsManual = d.ClockInIsManual, clockOutIsManual = d.ClockOutIsManual,
                        inProgress = d.InProgress, needsCorrection = d.NeedsCorrection,
                        workedMinutes = d.WorkedMinutes, expectedMinutes = d.ExpectedMinutes, balanceMinutes = d.BalanceMinutes,
                    },
                    monthToDate = new { workedMinutes = sheet.MonthToDate.WorkedMinutes, expectedMinutes = sheet.MonthToDate.ExpectedMinutes, balanceMinutes = sheet.MonthToDate.BalanceMinutes },
                };
            }
            return Results.Ok(new { saved = commit, before = Side(plan.Before), after = Side(plan.After) });
        }
        catch (AttendanceBusyException)
        {
            return Results.Json(new { error = "Your attendance is being updated. Nothing was saved; please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (CorrectionException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
    }

    private static DateTimeOffset? ToUtc(DateTime? local, int? offsetMinutes)
    {
        if (local is null) return null;
        if (local.Value.Kind != DateTimeKind.Unspecified)
            throw new InvalidLocalTimeException("Send Zurich local time without 'Z' or an offset, e.g. 2026-11-20T08:00:00.");
        return ZurichTime.LocalToUtc(local.Value, offsetMinutes);
    }
}
