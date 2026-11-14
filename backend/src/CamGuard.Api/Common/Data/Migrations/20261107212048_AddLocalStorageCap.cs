using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalStorageCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LocalMaxGb",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "LocalMaxGb",
                value: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocalMaxGb",
                table: "Settings");
        }
    }
}
