using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClockApp.Attendance.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCorrectionsAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_Kind",
                table: "AttendanceCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStartDate",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayType",
                table: "Users",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "AttendanceCorrections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "AttendanceCorrections",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<long>(
                name: "TargetCorrectionId",
                table: "AttendanceCorrections",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeScheduleVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedMinutesMon = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesTue = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesWed = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesThu = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesFri = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesSat = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedMinutesSun = table.Column<short>(type: "smallint", nullable: false),
                    AllowsOvernightShifts = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeScheduleVersions", x => x.Id);
                    table.CheckConstraint("CK_EmployeeScheduleVersions_AtLeastOneDay", "[ExpectedMinutesMon] + [ExpectedMinutesTue] + [ExpectedMinutesWed] + [ExpectedMinutesThu] + [ExpectedMinutesFri] + [ExpectedMinutesSat] + [ExpectedMinutesSun] > 0");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Fri", "[ExpectedMinutesFri] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Mon", "[ExpectedMinutesMon] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Sat", "[ExpectedMinutesSat] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Sun", "[ExpectedMinutesSun] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Thu", "[ExpectedMinutesThu] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Tue", "[ExpectedMinutesTue] BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_EmployeeScheduleVersions_Wed", "[ExpectedMinutesWed] BETWEEN 0 AND 1440");
                    table.ForeignKey(
                        name: "FK_EmployeeScheduleVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeScheduleVersions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PayType",
                table: "Users",
                sql: "[PayType] IN ('Monthly','Hourly')");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_BatchId",
                table: "AttendanceCorrections",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_TargetCorrectionId",
                table: "AttendanceCorrections",
                column: "TargetCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_UserId_TargetCorrectionId",
                table: "AttendanceCorrections",
                columns: new[] { "UserId", "TargetCorrectionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_Kind",
                table: "AttendanceCorrections",
                sql: "[Kind] IN ('Add','Adjust','Void')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections",
                sql: "([Kind] = 'Add' AND [AttendanceRecordId] IS NULL AND [TargetCorrectionId] IS NULL) OR ([Kind] IN ('Adjust','Void') AND (([AttendanceRecordId] IS NOT NULL AND [TargetCorrectionId] IS NULL) OR ([AttendanceRecordId] IS NULL AND [TargetCorrectionId] IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_ReasonCode",
                table: "AttendanceCorrections",
                sql: "[ReasonCode] IN ('ForgotClockIn','ForgotClockOut','WrongTimePunched','DuplicateOrAccidental','WorkedOffsite','SystemProblem','Other')");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeScheduleVersions_CreatedByUserId",
                table: "EmployeeScheduleVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeScheduleVersions_UserId_EffectiveFrom_Id",
                table: "EmployeeScheduleVersions",
                columns: new[] { "UserId", "EffectiveFrom", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrections_AttendanceCorrections_TargetCorrectionId",
                table: "AttendanceCorrections",
                column: "TargetCorrectionId",
                principalTable: "AttendanceCorrections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // --- Data steps (nothing existing is deleted or rewritten) -----------------------------------

            // ReasonCode was added with a temporary DEFAULT ('Other' for legacy rows). Attendance tables must not
            // keep any DEFAULT constraint (schema test), so drop it now.
            migrationBuilder.Sql(@"
DECLARE @n nvarchar(200) = (SELECT dc.name FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.AttendanceCorrections') AND c.name = N'ReasonCode');
IF @n IS NOT NULL EXEC(N'ALTER TABLE dbo.AttendanceCorrections DROP CONSTRAINT [' + @n + N']');");

            // Employment start date for existing employees: their earliest attendance date, else the migration date.
            migrationBuilder.Sql(@"
UPDATE u SET EmploymentStartDate = COALESCE(
    (SELECT MIN(d) FROM (SELECT MIN(WorkDate) AS d FROM AttendanceRecords WHERE UserId = u.Id
                         UNION ALL SELECT MIN(WorkDate) FROM AttendanceCorrections WHERE UserId = u.Id) x),
    CAST(SYSUTCDATETIME() AS date))
FROM Users u WHERE u.Role = 'Employee' AND u.EmploymentStartDate IS NULL;");

            // One initial schedule version per existing employee: Mon-Fri = the current company default, Sat/Sun = 0.
            migrationBuilder.Sql(@"
INSERT INTO EmployeeScheduleVersions
    (UserId, EffectiveFrom, ExpectedMinutesMon, ExpectedMinutesTue, ExpectedMinutesWed, ExpectedMinutesThu,
     ExpectedMinutesFri, ExpectedMinutesSat, ExpectedMinutesSun, AllowsOvernightShifts, CreatedByUserId, CreatedAtUtc)
SELECT u.Id, u.EmploymentStartDate, s.DurationMinutes, s.DurationMinutes, s.DurationMinutes, s.DurationMinutes,
       s.DurationMinutes, 0, 0, 0, COALESCE((SELECT MIN(Id) FROM Users WHERE Role = 'Admin'), u.Id), SYSUTCDATETIME()
FROM Users u CROSS JOIN ShiftSettings s
WHERE u.Role = 'Employee' AND NOT EXISTS (SELECT 1 FROM EmployeeScheduleVersions v WHERE v.UserId = u.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrections_AttendanceCorrections_TargetCorrectionId",
                table: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "EmployeeScheduleVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PayType",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrections_BatchId",
                table: "AttendanceCorrections");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrections_TargetCorrectionId",
                table: "AttendanceCorrections");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrections_UserId_TargetCorrectionId",
                table: "AttendanceCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_Kind",
                table: "AttendanceCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_ReasonCode",
                table: "AttendanceCorrections");

            migrationBuilder.DropColumn(
                name: "EmploymentStartDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PayType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "AttendanceCorrections");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "AttendanceCorrections");

            migrationBuilder.DropColumn(
                name: "TargetCorrectionId",
                table: "AttendanceCorrections");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_Kind",
                table: "AttendanceCorrections",
                sql: "[Kind] IN ('Add','Adjust')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections",
                sql: "([Kind] = 'Add' AND [AttendanceRecordId] IS NULL) OR ([Kind] = 'Adjust' AND [AttendanceRecordId] IS NOT NULL)");
        }
    }
}
