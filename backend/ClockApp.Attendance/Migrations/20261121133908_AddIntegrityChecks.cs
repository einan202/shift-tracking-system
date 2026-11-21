using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClockApp.Attendance.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceRecords_OpenHasNoOut",
                table: "AttendanceRecords",
                sql: "NOT ([IsOpen] = 1 AND [ClockOutUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections",
                sql: "([Kind] = 'Add' AND [AttendanceRecordId] IS NULL) OR ([Kind] = 'Adjust' AND [AttendanceRecordId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceRecords_OpenHasNoOut",
                table: "AttendanceRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttendanceCorrections_KindTarget",
                table: "AttendanceCorrections");
        }
    }
}
