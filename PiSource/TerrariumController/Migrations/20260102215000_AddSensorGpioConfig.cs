using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerrariumController.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorGpioConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Sensor1GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sensor2GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sensor3GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Sensor1GPIO", "Sensor2GPIO", "Sensor3GPIO" },
                values: new object[] { 23, 24, 25 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sensor1GPIO",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Sensor2GPIO",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Sensor3GPIO",
                table: "Settings");
        }
    }
}
