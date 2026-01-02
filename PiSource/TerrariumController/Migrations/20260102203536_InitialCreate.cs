using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerrariumController.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HumidityLockoutStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SensorId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTriggeredTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumidityLockoutStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogType = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    RelayId = table.Column<int>(type: "INTEGER", nullable: true),
                    RelayState = table.Column<bool>(type: "INTEGER", nullable: true),
                    Sensor1Temperature = table.Column<double>(type: "REAL", nullable: true),
                    Sensor1Humidity = table.Column<double>(type: "REAL", nullable: true),
                    Sensor2Temperature = table.Column<double>(type: "REAL", nullable: true),
                    Sensor2Humidity = table.Column<double>(type: "REAL", nullable: true),
                    Sensor3Temperature = table.Column<double>(type: "REAL", nullable: true),
                    Sensor3Humidity = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RelayStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RelayId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggerSource = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSensorId = table.Column<int>(type: "INTEGER", nullable: true),
                    SensorTemperature = table.Column<double>(type: "REAL", nullable: true),
                    SensorHumidity = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelayStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SensorReadings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SensorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Temperature = table.Column<double>(type: "REAL", nullable: true),
                    Humidity = table.Column<double>(type: "REAL", nullable: true),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensorReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Threshold1Temperature = table.Column<double>(type: "REAL", nullable: false),
                    Threshold2Temperature = table.Column<double>(type: "REAL", nullable: false),
                    Threshold3Temperature = table.Column<double>(type: "REAL", nullable: false),
                    Sensor1HumidityThreshold = table.Column<double>(type: "REAL", nullable: false),
                    TemperatureHysteresis = table.Column<double>(type: "REAL", nullable: false),
                    Relay4OnTime = table.Column<string>(type: "TEXT", nullable: false),
                    Relay4OffTime = table.Column<string>(type: "TEXT", nullable: false),
                    Relay1GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    Relay2GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    Relay3GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    Relay4GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    Relay5GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    Relay6GPIO = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraFramerate = table.Column<int>(type: "INTEGER", nullable: false),
                    LogRetentionMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    HumidityLockoutHours = table.Column<int>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "CameraFramerate", "CameraHeight", "CameraWidth", "HumidityLockoutHours", "LastModified", "LogRetentionMonths", "Relay1GPIO", "Relay2GPIO", "Relay3GPIO", "Relay4GPIO", "Relay4OffTime", "Relay4OnTime", "Relay5GPIO", "Relay6GPIO", "Sensor1HumidityThreshold", "TemperatureHysteresis", "Threshold1Temperature", "Threshold2Temperature", "Threshold3Temperature" },
                values: new object[] { 1, 15, 720, 1280, 6, new DateTime(2026, 1, 2, 20, 35, 35, 590, DateTimeKind.Utc).AddTicks(8702), 12, 29, 31, 33, 35, "20:00", "08:00", 37, 40, 60.0, 1.0, 29.0, 29.0, 29.0 });

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Timestamp",
                table: "LogEntries",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_RelayStates_Timestamp",
                table: "RelayStates",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SensorReadings_Timestamp",
                table: "SensorReadings",
                column: "Timestamp",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HumidityLockoutStates");

            migrationBuilder.DropTable(
                name: "LogEntries");

            migrationBuilder.DropTable(
                name: "RelayStates");

            migrationBuilder.DropTable(
                name: "SensorReadings");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
