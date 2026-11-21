using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClockApp.Attendance.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InviteTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InviteExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Role", "[Role] IN ('Admin','Employee')");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClockInUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClockOutUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeProvider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.CheckConstraint("CK_AttendanceRecords_OutAfterIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]");
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSettings", x => x.Id);
                    table.CheckConstraint("CK_ShiftSettings_Duration", "[DurationMinutes] BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_ShiftSettings_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_ShiftSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AttendanceRecordId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CorrectedClockInUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrectedClockOutUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousClockInUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousClockOutUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.CheckConstraint("CK_AttendanceCorrections_Kind", "[Kind] IN ('Add','Adjust')");
                    table.CheckConstraint("CK_AttendanceCorrections_Reason", "LEN(LTRIM(RTRIM([Reason]))) >= 5");
                    table.CheckConstraint("CK_AttendanceCorrections_Times", "[CorrectedClockOutUtc] IS NULL OR [CorrectedClockInUtc] IS NULL OR [CorrectedClockOutUtc] > [CorrectedClockInUtc]");
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_AttendanceRecords_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ShiftSettings",
                columns: new[] { "Id", "DurationMinutes", "UpdatedByUserId" },
                values: new object[] { 1, 480, null });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AttendanceRecordId",
                table: "AttendanceCorrections",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_CreatedByUserId",
                table: "AttendanceCorrections",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_UserId_WorkDate",
                table: "AttendanceCorrections",
                columns: new[] { "UserId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_UserId_WorkDate",
                table: "AttendanceRecords",
                columns: new[] { "UserId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "UX_OneOpenPerUser",
                table: "AttendanceRecords",
                column: "UserId",
                unique: true,
                filter: "[IsOpen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSettings_UpdatedByUserId",
                table: "ShiftSettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "ShiftSettings");

            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
