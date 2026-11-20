using ClockApp.Api.Data;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Data;

public class AdminSeederTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();

    [RequiresDbFact]
    public async Task Seeds_one_admin_with_a_hashed_password_and_is_idempotent()
    {
        await using var db = fixture.CreateContext();
        const string password = "correct-horse-battery";

        Assert.True(await AdminSeeder.SeedAsync(db, Hasher, "seed-admin@test.local", password));
        Assert.False(await AdminSeeder.SeedAsync(db, Hasher, "another-admin@test.local", password));

        var admin = await db.Users.SingleAsync(u => u.Role == UserRole.Admin);
        Assert.Equal("seed-admin@test.local", admin.Email);
        Assert.True(admin.IsActive);
        Assert.NotNull(admin.PasswordHash);
        Assert.DoesNotContain(password, admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(admin, admin.PasswordHash!, password));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(admin, admin.PasswordHash!, "wrong-password-1"));
    }

    [RequiresDbFact]
    public async Task Rejects_short_passwords_and_blank_emails()
    {
        await using var db = fixture.CreateContext();

        await Assert.ThrowsAsync<ArgumentException>(() => AdminSeeder.SeedAsync(db, Hasher, "short@test.local", "short"));
        await Assert.ThrowsAsync<ArgumentException>(() => AdminSeeder.SeedAsync(db, Hasher, " ", "long-enough-password"));
    }
}
