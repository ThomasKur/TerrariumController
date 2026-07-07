using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerrariumController.Migrations
{
    public partial class RemoveSensorGpioSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_Sensor1GPIO_Sensor2GPIO_Sensor3GPIO",
                table: "Settings");

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Sensor1GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 16);

            migrationBuilder.AddColumn<int>(
                name: "Sensor2GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "Sensor3GPIO",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 22);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Sensor1GPIO_Sensor2GPIO_Sensor3GPIO",
                table: "Settings",
                columns: new[] { "Sensor1GPIO", "Sensor2GPIO", "Sensor3GPIO" },
                unique: true);
        }
    }
}
