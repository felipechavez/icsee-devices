using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cameras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Icsee"),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DvripPort = table.Column<int>(type: "integer", nullable: false),
                    Go2rtcStream = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordProtected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TuyaDeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TuyaTriggerDeviceIds = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Rotation = table.Column<int>(type: "integer", nullable: false),
                    MotionSensitivity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SensitivityPending = table.Column<bool>(type: "boolean", nullable: false),
                    BatteryPercent = table.Column<int>(type: "integer", nullable: true),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    DeviceInfoAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cameras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MaxClipSeconds = table.Column<int>(type: "integer", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CameraEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CameraEvents_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSec = table.Column<double>(type: "double precision", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ThumbnailKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recordings_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "CooldownSeconds", "MaxClipSeconds", "RetentionDays" },
                values: new object[] { 1, 10, 60, 30 });

            migrationBuilder.CreateIndex(
                name: "IX_CameraEvents_CameraId",
                table: "CameraEvents",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_CameraEvents_OccurredAt",
                table: "CameraEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_Go2rtcStream",
                table: "Cameras",
                column: "Go2rtcStream",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_TuyaDeviceId",
                table: "Cameras",
                column: "TuyaDeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_CameraId_StartedAt",
                table: "Recordings",
                columns: new[] { "CameraId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_StartedAt",
                table: "Recordings",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CameraEvents");

            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Cameras");
        }
    }
}
