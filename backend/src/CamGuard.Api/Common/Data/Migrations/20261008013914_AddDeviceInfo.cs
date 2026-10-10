using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatteryPercent",
                table: "Cameras",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeviceInfoAt",
                table: "Cameras",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Cameras",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotionSensitivity",
                table: "Cameras",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Medium");

            migrationBuilder.AddColumn<bool>(
                name: "SensitivityPending",
                table: "Cameras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "StorageTotalBytes",
                table: "Cameras",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageUsedBytes",
                table: "Cameras",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatteryPercent",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "DeviceInfoAt",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "MotionSensitivity",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "SensitivityPending",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "StorageTotalBytes",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "StorageUsedBytes",
                table: "Cameras");
        }
    }
}
