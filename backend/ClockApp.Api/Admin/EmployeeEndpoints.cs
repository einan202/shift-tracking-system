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
    }
}
