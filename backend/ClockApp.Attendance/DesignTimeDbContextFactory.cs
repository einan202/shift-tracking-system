using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClockApp.Attendance;

/// <summary>Used only by `dotnet ef` to create migrations; no database connection is required for that.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AttendanceDbContext>
{
    public AttendanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseSqlServer("Server=localhost;Database=ClockApp_Design;Integrated Security=true;TrustServerCertificate=True")
            .Options;
        return new AttendanceDbContext(options);
    }
}
