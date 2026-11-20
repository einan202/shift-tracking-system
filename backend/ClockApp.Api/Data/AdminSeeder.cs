using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Api.Data;

public static class AdminSeeder
{
    public const int MinPasswordLength = 10;

    /// <summary>
    /// Creates the first admin from configuration. Does nothing if any admin already exists,
    /// so it is safe to run on every startup. The password is stored only as a PasswordHasher hash.
    /// </summary>
    /// <returns>True when an admin was created.</returns>
    public static async Task<bool> SeedAsync(
        AttendanceDbContext db, IPasswordHasher<User> hasher, string email, string password, string fullName = "Administrator")
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Seed admin email is required.", nameof(email));
        if (password is null || password.Length < MinPasswordLength)
            throw new ArgumentException($"Seed admin password must be at least {MinPasswordLength} characters.", nameof(password));

        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin)) return false;

        var admin = new User
        {
            Email = email.Trim(),
            FullName = fullName,
            Role = UserRole.Admin,
            IsActive = true,
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return true;
    }
}
