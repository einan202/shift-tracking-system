using ClockApp.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

public class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : DbContext(options)
{
    public const int DefaultShiftMinutes = 480;

    public DbSet<User> Users => Set<User>();
    public DbSet<ShiftSettings> ShiftSettings => Set<ShiftSettings>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<EmployeeScheduleVersion> EmployeeScheduleVersions => Set<EmployeeScheduleVersion>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

    // NOTE: no database DEFAULT expressions (GETDATE/SYSUTCDATETIME...) on any attendance time column.
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users", t =>
            {
                t.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Admin','Employee')");
                t.HasCheckConstraint("CK_Users_PayType", "[PayType] IN ('Monthly','Hourly')");
            });
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256);
            e.Property(x => x.InviteTokenHash).HasMaxLength(64);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PayType).HasConversion<string>().HasMaxLength(10);
        });

        // Append-only schedule versions (INSERT/SELECT only for the app user). A change is a new row.
        b.Entity<EmployeeScheduleVersion>(e =>
        {
            e.ToTable("EmployeeScheduleVersions", t =>
            {
                foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" })
                    t.HasCheckConstraint($"CK_EmployeeScheduleVersions_{day}", $"[ExpectedMinutes{day}] BETWEEN 0 AND 1440");
                t.HasCheckConstraint("CK_EmployeeScheduleVersions_AtLeastOneDay",
                    "[ExpectedMinutesMon] + [ExpectedMinutesTue] + [ExpectedMinutesWed] + [ExpectedMinutesThu] + [ExpectedMinutesFri] + [ExpectedMinutesSat] + [ExpectedMinutesSun] > 0");
            });
            e.HasIndex(x => new { x.UserId, x.EffectiveFrom, x.Id });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ShiftSettings>(e =>
        {
            e.ToTable("ShiftSettings", t =>
            {
                t.HasCheckConstraint("CK_ShiftSettings_Singleton", "[Id] = 1");
                t.HasCheckConstraint("CK_ShiftSettings_Duration", "[DurationMinutes] BETWEEN 1 AND 1440");
                t.HasCheckConstraint("CK_ShiftSettings_Week", "[ExpectedMinutesMon] BETWEEN 0 AND 1440 AND [ExpectedMinutesTue] BETWEEN 0 AND 1440 AND [ExpectedMinutesWed] BETWEEN 0 AND 1440 AND [ExpectedMinutesThu] BETWEEN 0 AND 1440 AND [ExpectedMinutesFri] BETWEEN 0 AND 1440 AND [ExpectedMinutesSat] BETWEEN 0 AND 1440 AND [ExpectedMinutesSun] BETWEEN 0 AND 1440 AND ([ExpectedMinutesMon] + [ExpectedMinutesTue] + [ExpectedMinutesWed] + [ExpectedMinutesThu] + [ExpectedMinutesFri] + [ExpectedMinutesSat] + [ExpectedMinutesSun]) > 0");
            });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Week);
            e.HasData(new Entities.ShiftSettings
            {
                Id = Entities.ShiftSettings.SingletonId, DurationMinutes = DefaultShiftMinutes,
                ExpectedMinutesMon = DefaultShiftMinutes, ExpectedMinutesTue = DefaultShiftMinutes, ExpectedMinutesWed = DefaultShiftMinutes,
                ExpectedMinutesThu = DefaultShiftMinutes, ExpectedMinutesFri = DefaultShiftMinutes,
            });
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("AttendanceRecords", t =>
            {
                t.HasCheckConstraint("CK_AttendanceRecords_OutAfterIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]");
                // An open record has no clock-out yet. (A record closed by an admin correction has IsOpen = 0 and may keep ClockOutUtc NULL.)
                t.HasCheckConstraint("CK_AttendanceRecords_OpenHasNoOut", "NOT ([IsOpen] = 1 AND [ClockOutUtc] IS NOT NULL)");
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
                t.HasCheckConstraint("CK_AttendanceCorrections_Kind", "[Kind] IN ('Add','Adjust','Void')");
                // 'Add' creates a missing record (no target); 'Adjust' and 'Void' target exactly one thing: a punch OR a manually added interval.
                t.HasCheckConstraint("CK_AttendanceCorrections_KindTarget",
                    "([Kind] = 'Add' AND [AttendanceRecordId] IS NULL AND [TargetCorrectionId] IS NULL) OR " +
                    "([Kind] IN ('Adjust','Void') AND (([AttendanceRecordId] IS NOT NULL AND [TargetCorrectionId] IS NULL) OR ([AttendanceRecordId] IS NULL AND [TargetCorrectionId] IS NOT NULL)))");
                t.HasCheckConstraint("CK_AttendanceCorrections_ReasonCode",
                    "[ReasonCode] IN ('ForgotClockIn','ForgotClockOut','WrongTimePunched','DuplicateOrAccidental','WorkedOffsite','SystemProblem','Other')");
                t.HasCheckConstraint("CK_AttendanceCorrections_Reason", "LEN(LTRIM(RTRIM([Reason]))) >= 5");
                t.HasCheckConstraint("CK_AttendanceCorrections_Times", "[CorrectedClockOutUtc] IS NULL OR [CorrectedClockInUtc] IS NULL OR [CorrectedClockOutUtc] > [CorrectedClockInUtc]");
            });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.UserId, x.WorkDate });
            e.HasIndex(x => new { x.UserId, x.TargetCorrectionId });
            e.HasIndex(x => x.BatchId);
            e.HasOne<AttendanceCorrection>().WithMany().HasForeignKey(x => x.TargetCorrectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
