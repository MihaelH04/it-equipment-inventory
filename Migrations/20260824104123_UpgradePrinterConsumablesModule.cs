using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class UpgradePrinterConsumablesModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorStateJson",
                table: "PrinterConsumables",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConsumablePendingOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrinterConsumableId = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityOrdered = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityReceived = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OrderedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumablePendingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumablePendingOrders_PrinterConsumables_PrinterConsumableId",
                        column: x => x.PrinterConsumableId,
                        principalTable: "PrinterConsumables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumablePendingOrders_OrderedAt",
                table: "ConsumablePendingOrders",
                column: "OrderedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumablePendingOrders_OrderGroupId",
                table: "ConsumablePendingOrders",
                column: "OrderGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumablePendingOrders_PrinterConsumableId_CompletedAt",
                table: "ConsumablePendingOrders",
                columns: new[] { "PrinterConsumableId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumablePendingOrders");

            migrationBuilder.DropColumn(
                name: "ColorStateJson",
                table: "PrinterConsumables");
        }
    }
}
