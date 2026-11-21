using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClockApp.Attendance.Migrations
{
    /// <inheritdoc />
    public partial class ShiftSettingsPerWeekday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesFri",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesMon",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesSat",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesSun",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesThu",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesTue",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "ExpectedMinutesWed",
                table: "ShiftSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            // Keep the admin's current single default: Monday-Friday take it, the weekend stays off.
            migrationBuilder.Sql(@"UPDATE ShiftSettings SET ExpectedMinutesMon = DurationMinutes, ExpectedMinutesTue = DurationMinutes,
                ExpectedMinutesWed = DurationMinutes, ExpectedMinutesThu = DurationMinutes, ExpectedMinutesFri = DurationMinutes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftSettings_Week",
                table: "ShiftSettings",
                sql: "[ExpectedMinutesMon] BETWEEN 0 AND 1440 AND [ExpectedMinutesTue] BETWEEN 0 AND 1440 AND [ExpectedMinutesWed] BETWEEN 0 AND 1440 AND [ExpectedMinutesThu] BETWEEN 0 AND 1440 AND [ExpectedMinutesFri] BETWEEN 0 AND 1440 AND [ExpectedMinutesSat] BETWEEN 0 AND 1440 AND [ExpectedMinutesSun] BETWEEN 0 AND 1440 AND ([ExpectedMinutesMon] + [ExpectedMinutesTue] + [ExpectedMinutesWed] + [ExpectedMinutesThu] + [ExpectedMinutesFri] + [ExpectedMinutesSat] + [ExpectedMinutesSun]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftSettings_Week",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesFri",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesMon",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesSat",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesSun",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesThu",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesTue",
                table: "ShiftSettings");

            migrationBuilder.DropColumn(
                name: "ExpectedMinutesWed",
                table: "ShiftSettings");
        }
    }
}
