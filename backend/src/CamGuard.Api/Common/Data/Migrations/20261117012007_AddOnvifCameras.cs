using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnvifCameras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnvifMainCodec",
                table: "Cameras",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnvifMainStreamUri",
                table: "Cameras",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OnvifPort",
                table: "Cameras",
                type: "integer",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<string>(
                name: "OnvifSubStreamUri",
                table: "Cameras",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnvifMainCodec",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "OnvifMainStreamUri",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "OnvifPort",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "OnvifSubStreamUri",
                table: "Cameras");
        }
    }
}
