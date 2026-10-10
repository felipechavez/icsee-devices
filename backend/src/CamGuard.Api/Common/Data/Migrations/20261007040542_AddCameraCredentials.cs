using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordProtected",
                table: "Cameras",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Cameras",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_Go2rtcStream",
                table: "Cameras",
                column: "Go2rtcStream",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cameras_Go2rtcStream",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "PasswordProtected",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Cameras");
        }
    }
}
