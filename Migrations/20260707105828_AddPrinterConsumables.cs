using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterConsumables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrinterConsumables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityAvailable = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityOrdered = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsOriginal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterConsumables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterConsumables_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterConsumables_EquipmentId",
                table: "PrinterConsumables",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterConsumables_IsActive",
                table: "PrinterConsumables",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterConsumables_Name",
                table: "PrinterConsumables",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterConsumables_ProductCode",
                table: "PrinterConsumables",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterConsumables_Type_Color_IsActive",
                table: "PrinterConsumables",
                columns: new[] { "Type", "Color", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterConsumables");
        }
    }
}
