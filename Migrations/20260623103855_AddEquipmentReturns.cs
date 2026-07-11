using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    InventoryNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    EquipmentType = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousSiteId = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousSiteCode = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousSiteName = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousSiteLocation = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousEmployeeId = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousEmployeeCode = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousEmployeeName = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HandedOverBy = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentReturns", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentReturns");
        }
    }
}
