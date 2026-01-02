using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerrariumController.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastModified",
                value: new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastModified",
                value: new DateTime(2026, 1, 2, 20, 35, 35, 590, DateTimeKind.Utc).AddTicks(8702));
        }
    }
}
