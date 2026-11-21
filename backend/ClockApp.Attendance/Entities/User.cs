namespace ClockApp.Attendance.Entities;

public enum UserRole { Admin, Employee }

/// <summary>How the employee is paid. Hourly is stored for the future but not offered yet.</summary>
public enum PayType { Monthly, Hourly }

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";

    /// <summary>Output of ASP.NET PasswordHasher; null until the employee sets a password via the invite link.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>SHA-256 of the one-time invite/reset token (never the token itself).</summary>
    public string? InviteTokenHash { get; set; }
    public DateTimeOffset? InviteExpiresUtc { get; set; }

    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    public PayType PayType { get; set; } = PayType.Monthly;

    /// <summary>First day of employment (employees only). Months before it are never required or expected.</summary>
    public DateOnly? EmploymentStartDate { get; set; }
}
