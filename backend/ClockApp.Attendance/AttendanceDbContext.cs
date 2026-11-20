using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

public class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : DbContext(options)
{
    public const int DefaultShiftMinutes = 480;

    public DbSet<User> Users => Set<User>();
    public DbSet<ShiftSettings> ShiftSettings => Set<ShiftSettings>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

    // NOTE: no database DEFAULT expressions (GETDATE/SYSUTCDATETIME...) on any attendance time column.
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users", t =>
            {
                t.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Admin','Employee')");
            });
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256);
            e.Property(x => x.InviteTokenHash).HasMaxLength(64);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ShiftSettings>(e =>
        {
            e.ToTable("ShiftSettings", t =>
            {
                t.HasCheckConstraint("CK_ShiftSettings_Singleton", "[Id] = 1");
                t.HasCheckConstraint("CK_ShiftSettings_Duration", "[DurationMinutes] BETWEEN 1 AND 1440");
            });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasData(new Entities.ShiftSettings { Id = Entities.ShiftSettings.SingletonId, DurationMinutes = DefaultShiftMinutes });
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("AttendanceRecords", t =>
            {
                t.HasCheckConstraint("CK_AttendanceRecords_OutAfterIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]");
            });
            e.Property(x => x.TimeProvider).HasMaxLength(30).IsRequired();
            // At most one open record per user, enforced by the database (strict option A).
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("[IsOpen] = 1").HasDatabaseName("UX_OneOpenPerUser");
            e.HasIndex(x => new { x.UserId, x.WorkDate });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AttendanceCorrection>(e =>
        {
            e.ToTable("AttendanceCorrections", t =>
            {
                t.HasCheckConstraint("CK_AttendanceCorrections_Kind", "[Kind] IN ('Add','Adjust')");
                t.HasCheckConstraint("CK_AttendanceCorrections_Reason", "LEN(LTRIM(RTRIM([Reason]))) >= 5");
                t.HasCheckConstraint("CK_AttendanceCorrections_Times", "[CorrectedClockOutUtc] IS NULL OR [CorrectedClockInUtc] IS NULL OR [CorrectedClockOutUtc] > [CorrectedClockInUtc]");
            });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.UserId, x.WorkDate });
            e.HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
