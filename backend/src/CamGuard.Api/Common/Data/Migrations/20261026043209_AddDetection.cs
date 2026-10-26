using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CamGuard.Api.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Valores por defecto puestos a mano: solo completan las filas existentes, que quedan como una
            // cámara nueva (modo Tag, personas y animales). Al insertar, EF siempre envía el valor.
            migrationBuilder.AddColumn<double>(
                name: "DetectionMinConfidence",
                table: "Settings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5);

            migrationBuilder.AddColumn<string>(
                name: "Detection",
                table: "Recordings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DetectionScore",
                table: "Recordings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DetectAnimals",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DetectPeople",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectionMode",
                table: "Cameras",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Tag");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DetectionMinConfidence",
                value: 0.5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectionMinConfidence",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Detection",
                table: "Recordings");

            migrationBuilder.DropColumn(
                name: "DetectionScore",
                table: "Recordings");

            migrationBuilder.DropColumn(
                name: "DetectAnimals",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "DetectPeople",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "DetectionMode",
                table: "Cameras");
        }
    }
}
