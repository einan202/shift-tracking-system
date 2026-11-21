using System.Net.Mail;
using ClockApp.Api.Auth;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Admin;

public sealed record CreateEmployeeRequest(string? Email, string? FullName);

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/employees").RequireAuthorization(Policies.Admin);

        // Admin creates the employee; the system generates a one-time invite token. The admin never
        // sees or chooses a password. The raw token is returned once and only its hash is stored.
        group.MapPost("", async (CreateEmployeeRequest req, AttendanceDbContext db, TimeProvider clock) =>
        {
            var email = req.Email?.Trim();
            var name = req.FullName?.Trim();
            if (string.IsNullOrEmpty(email) || email.Length > 256 || !MailAddress.TryCreate(email, out _))
                return Results.BadRequest(new { error = "A valid email is required." });
            if (string.IsNullOrEmpty(name) || name.Length > 200)
                return Results.BadRequest(new { error = "Full name is required (max 200 characters)." });
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "An employee with this email already exists." });

            // Role is fixed server-side: this endpoint can only ever create Employees.
            var user = new User { Email = email, FullName = name, Role = UserRole.Employee, IsActive = true };
            var rawToken = InviteTokens.Issue(user, clock.GetUtcNow());
            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Created($"/api/admin/employees/{user.Id}", new
            {
                id = user.Id,
                email = user.Email,
                fullName = user.FullName,
                inviteToken = rawToken,
                invitePath = $"/set-password?token={rawToken}",
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
                hasPassword = u.PasswordHash != null,
                invitePending = u.InviteTokenHash != null,
                inviteExpiresUtc = u.InviteExpiresUtc,
            }).ToListAsync()));

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
                invitePath = $"/set-password?token={rawToken}",
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
