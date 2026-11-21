using System.Security.Claims;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Admin;

/// <summary>
/// Admin-typed corrections. Times arrive as Zurich wall-clock values (plus an offset in minutes only to
/// disambiguate the repeated fall-back hour) and are stored as UTC.
/// </summary>
public sealed record CorrectionDto(
    string? Kind, int UserId, long? AttendanceRecordId,
    DateTime? ClockInLocal, DateTime? ClockOutLocal,
    int? ClockInOffsetMinutes, int? ClockOutOffsetMinutes,
    string? Reason);

public static class AdminAttendanceEndpoints
{
    public static void MapAdminAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/attendance").RequireAuthorization(Policies.Admin);

        // Original punches + effective values + full correction history for one employee and date range.
        group.MapGet("", async (int userId, DateOnly? from, DateOnly? to, AttendanceDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.Role == UserRole.Employee, ct))
                return Results.NotFound(new { error = "Employee not found." });

            // Default range = the current Zurich month. Server clock is fine here: nothing is recorded.
            var today = ZurichTime.LocalDateOf(clock.GetUtcNow());
            var f = from ?? new DateOnly(today.Year, today.Month, 1);
            var t = to ?? f.AddMonths(1).AddDays(-1);

            var effective = await EffectiveAttendance.LoadAsync(db, userId, f, t, ct, includeVoided: true);   // admins also see deleted shifts
            var corrections = await db.AttendanceCorrections.AsNoTracking()
                .Where(c => c.UserId == userId).OrderBy(c => c.Id).ToListAsync(ct);
            var adminNames = await db.Users.AsNoTracking()
                .Where(u => corrections.Select(c => c.CreatedByUserId).Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

            var rows = effective.Select(e => new
            {
                recordId = e.RecordId,
                source = e.IsManual ? "manual" : "punch",
                original = e.OriginalIn is null ? null : new { clockInUtc = e.OriginalIn, clockOutUtc = e.OriginalOut },
                effective = new { clockInUtc = e.In, clockOutUtc = e.Out, workDate = e.WorkDate },
                isAdjusted = e.IsAdjusted,
                isVoided = e.IsVoided,
                isOpen = e.IsOpen,
                history = corrections
                    .Where(c => e.IsManual ? (c.Id == e.AddCorrectionId || c.TargetCorrectionId == e.AddCorrectionId) : c.AttendanceRecordId == e.RecordId)
                    .Select(c => new
                    {
                        id = c.Id,
                        kind = c.Kind.ToString(),
                        previousClockInUtc = c.PreviousClockInUtc,
                        previousClockOutUtc = c.PreviousClockOutUtc,
                        correctedClockInUtc = c.CorrectedClockInUtc,
                        correctedClockOutUtc = c.CorrectedClockOutUtc,
                        reason = c.Reason,
                        createdBy = adminNames.GetValueOrDefault(c.CreatedByUserId),
                        createdAtUtc = c.CreatedAtUtc,
                    }),
            });
            // Zurich "now" lets the UI default and cap the correction form in Zurich time, whatever the admin's
            // browser time zone is (server clock: used for UI defaults/validation only, nothing is recorded).
            var zurichNow = ZurichTime.ToLocal(clock.GetUtcNow()).ToString("yyyy-MM-dd'T'HH:mm");
            return Results.Ok(new { from = f, to = t, zurichNow, records = rows });
        });

        group.MapPost("corrections", async (CorrectionDto dto, ClaimsPrincipal admin, CorrectionService svc, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Enum.TryParse<CorrectionKind>(dto.Kind, ignoreCase: true, out var kind))
                return Results.BadRequest(new { error = "Kind must be 'Add' or 'Adjust'." });

            DateTimeOffset? clockIn, clockOut;
            try
            {
                clockIn = ToUtc(dto.ClockInLocal, dto.ClockInOffsetMinutes);
                clockOut = ToUtc(dto.ClockOutLocal, dto.ClockOutOffsetMinutes);
            }
            catch (InvalidLocalTimeException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message });
            }

            try
            {
                var adminId = int.Parse(admin.FindFirstValue(TokenService.SubjectClaim)!);
                // CreatedAtUtc / "not in the future" use the SERVER clock, supplied here (allowed: this is audit
                // metadata and validation, not a Clock In/Out record).
                var created = await svc.CreateAsync(adminId,
                    new CorrectionRequest(kind, dto.UserId, dto.AttendanceRecordId, clockIn, clockOut, dto.Reason),
                    clock.GetUtcNow(), ct);
                return Results.Created($"/api/admin/attendance?userId={created.UserId}", new
                {
                    id = created.Id,
                    kind = created.Kind.ToString(),
                    workDate = created.WorkDate,
                    correctedClockInUtc = created.CorrectedClockInUtc,
                    correctedClockOutUtc = created.CorrectedClockOutUtc,
                });
            }
            catch (AttendanceBusyException)
            {
                return Results.Json(new { error = "This employee's attendance is being updated. Nothing was saved; please try again." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (CorrectionException ex) when (ex.Error == CorrectionError.NotFound)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (CorrectionException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message });
            }
        });
    }

    private static DateTimeOffset? ToUtc(DateTime? local, int? offsetMinutes)
    {
        if (local is null) return null;
        // A "Z"/offset in the JSON would have been silently converted by the parser; require a plain local time.
        if (local.Value.Kind != DateTimeKind.Unspecified)
            throw new InvalidLocalTimeException("Send Zurich local time without 'Z' or an offset, e.g. 2026-11-20T08:00:00.");
        return ZurichTime.LocalToUtc(local.Value, offsetMinutes);
    }
}
