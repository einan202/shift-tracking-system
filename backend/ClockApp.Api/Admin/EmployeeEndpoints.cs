using System.Net.Mail;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using System.Security.Claims;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Admin;

/// <summary>Expected minutes Monday..Sunday (0 = not a working day) and the overnight setting.</summary>
public sealed record ScheduleDto(int[]? Minutes, bool? AllowsOvernightShifts);

public sealed record CreateEmployeeRequest(string? Email, string? FullName, DateOnly? EmploymentStartDate = null, ScheduleDto? Schedule = null);

/// <summary>A new schedule version; it must take effect from the first day of the current Zurich month or later.</summary>
public sealed record ChangeScheduleRequest(DateOnly? EffectiveFrom, int[]? Minutes, bool? AllowsOvernightShifts);

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/employees").RequireAuthorization(Policies.Admin);

        // Admin creates the employee; the system generates a one-time invite token. The admin never
        // sees or chooses a password. The raw token is returned once and only its hash is stored.
        group.MapPost("", async (CreateEmployeeRequest req, ClaimsPrincipal admin, AttendanceDbContext db, TimeProvider clock) =>
        {
            var email = req.Email?.Trim();
            var name = req.FullName?.Trim();
            if (string.IsNullOrEmpty(email) || email.Length > 256 || !MailAddress.TryCreate(email, out _))
                return Results.BadRequest(new { error = "A valid email is required." });
            if (string.IsNullOrEmpty(name) || name.Length > 200)
                return Results.BadRequest(new { error = "Full name is required (max 200 characters)." });
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "An employee with this email already exists." });

            // Initial schedule (effective from the employment start date). Defaults: today's Zurich date and the company
            // default duration Monday-Friday.
            var startDate = req.EmploymentStartDate ?? ZurichTime.LocalDateOf(clock.GetUtcNow());
            var minutes = req.Schedule?.Minutes ?? (await db.ShiftSettings.AsNoTracking().SingleAsync()).Week;
            var problem = ScheduleResolver.Validate(minutes);
            if (problem is not null) return Results.BadRequest(new { error = problem });

            // Role is fixed server-side: this endpoint can only ever create Employees.
            var user = new User { Email = email, FullName = name, Role = UserRole.Employee, IsActive = true, EmploymentStartDate = startDate };
            var rawToken = InviteTokens.Issue(user, clock.GetUtcNow());
            // The user and the first schedule version are saved together: no employee exists without a schedule.
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var version = new EmployeeScheduleVersion
            {
                UserId = user.Id, EffectiveFrom = startDate, AllowsOvernightShifts = req.Schedule?.AllowsOvernightShifts ?? false,
                CreatedByUserId = int.Parse(admin.FindFirstValue(TokenService.SubjectClaim)!), CreatedAtUtc = clock.GetUtcNow(),
            };
            ScheduleResolver.Apply(version, minutes);
            db.EmployeeScheduleVersions.Add(version);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return Results.Created($"/api/admin/employees/{user.Id}", new
            {
                id = user.Id,
                email = user.Email,
                fullName = user.FullName,
                employmentStartDate = user.EmploymentStartDate,
                inviteToken = rawToken,
                // Fragment, not query: a fragment is never sent to the server, so the token stays out of access logs.
                invitePath = $"/set-password#token={rawToken}",
                inviteExpiresUtc = user.InviteExpiresUtc,
            });
        });

        group.MapGet("", async (AttendanceDbContext db) => Results.Ok(await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Employee).OrderBy(u => u.FullName)
            .Select(u => new
            {
                id = u.Id,
                email = u.Email,
                fullName = u.FullName,
                isActive = u.IsActive,
                employmentStartDate = u.EmploymentStartDate,
                hasPassword = u.PasswordHash != null,
                invitePending = u.InviteTokenHash != null,
                inviteExpiresUtc = u.InviteExpiresUtc,
            }).ToListAsync()));

        // Schedule history (append-only versions) and "change schedule from [date]".
        group.MapGet("{id:int}/schedule", async (int id, AttendanceDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee, ct);
            if (user is null) return Results.NotFound(new { error = "Employee not found." });
            var versions = await db.EmployeeScheduleVersions.AsNoTracking().Where(v => v.UserId == id)
                .OrderBy(v => v.EffectiveFrom).ThenBy(v => v.Id).ToListAsync(ct);
            var names = await db.Users.AsNoTracking().Where(u => versions.Select(v => v.CreatedByUserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            return Results.Ok(new
            {
                employmentStartDate = user.EmploymentStartDate,
                versions = versions.Select(v => new
                {
                    id = v.Id, effectiveFrom = v.EffectiveFrom, minutes = ScheduleResolver.Weekdays(v),
                    allowsOvernightShifts = v.AllowsOvernightShifts, createdBy = names.GetValueOrDefault(v.CreatedByUserId), createdAtUtc = v.CreatedAtUtc,
                }),
            });
        });

        group.MapPost("{id:int}/schedule", async (int id, ChangeScheduleRequest req, ClaimsPrincipal admin, AttendanceDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id && u.Role == UserRole.Employee, ct))
                return Results.NotFound(new { error = "Employee not found." });
            var problem = ScheduleResolver.Validate(req.Minutes);
            if (problem is not null) return Results.BadRequest(new { error = problem });
            if (req.EffectiveFrom is not { } from) return Results.BadRequest(new { error = "An effective date is required." });

            // Prospective only: a change can never be effective before the first day of the current Zurich month,
            // so past months are never recalculated.
            var today = ZurichTime.LocalDateOf(clock.GetUtcNow());
            var firstAllowed = new DateOnly(today.Year, today.Month, 1);
            if (from < firstAllowed)
                return Results.UnprocessableEntity(new { error = $"A schedule change must take effect from {firstAllowed:yyyy-MM-dd} (the first day of the current month) or later; past months are never recalculated." });

            var version = new EmployeeScheduleVersion
            {
                UserId = id, EffectiveFrom = from, AllowsOvernightShifts = req.AllowsOvernightShifts ?? false,
                CreatedByUserId = int.Parse(admin.FindFirstValue(TokenService.SubjectClaim)!), CreatedAtUtc = clock.GetUtcNow(),
            };
            ScheduleResolver.Apply(version, req.Minutes!);
            db.EmployeeScheduleVersions.Add(version);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/employees/{id}/schedule", new { id = version.Id, effectiveFrom = version.EffectiveFrom, minutes = req.Minutes });
        });

        // Deactivation takes effect immediately: the per-request IsActive check rejects the user's existing tokens.
        group.MapPost("{id:int}/deactivate", (int id, AttendanceDbContext db) => SetActive(db, id, false));
        group.MapPost("{id:int}/activate", (int id, AttendanceDbContext db) => SetActive(db, id, true));

        // Forgotten password / lost link: the admin issues a NEW one-time link (replaces any previous token).
        // The current password stays valid until the employee sets a new one through the link.
        group.MapPost("{id:int}/invite", async (int id, AttendanceDbContext db, TimeProvider clock) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);
            if (user is null) return Results.NotFound(new { error = "Employee not found." });
            var rawToken = InviteTokens.Issue(user, clock.GetUtcNow());
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                inviteToken = rawToken,
                invitePath = $"/set-password#token={rawToken}",
                inviteExpiresUtc = user.InviteExpiresUtc,
            });
        });
    }

    private static async Task<IResult> SetActive(AttendanceDbContext db, int id, bool active)
    {
        // Only employees can be (de)activated here, so an admin can never lock out the last admin.
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);
        if (user is null) return Results.NotFound(new { error = "Employee not found." });
        user.IsActive = active;
        await db.SaveChangesAsync();
        return Results.Ok(new { id = user.Id, isActive = user.IsActive });
    }
}
