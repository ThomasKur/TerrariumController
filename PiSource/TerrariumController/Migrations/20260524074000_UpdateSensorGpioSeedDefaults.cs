using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerrariumController.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSensorGpioSeedDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Sensor1GPIO", "Sensor2GPIO", "Sensor3GPIO" },
                values: new object[] { 16, 15, 22 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Sensor1GPIO", "Sensor2GPIO", "Sensor3GPIO" },
                values: new object[] { 23, 24, 25 });
        }
    }
}