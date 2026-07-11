using ITEquipmentInventory.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260709110000_AddConsumableTransactionSiteTracking")]
public partial class AddConsumableTransactionSiteTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SiteId",
            table: "ConsumableTransactions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SiteName",
            table: "ConsumableTransactions",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ConsumableTransactions_SiteId",
            table: "ConsumableTransactions",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ConsumableTransactions_SiteName",
            table: "ConsumableTransactions",
            column: "SiteName");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ConsumableTransactions_SiteId",
            table: "ConsumableTransactions");

        migrationBuilder.DropIndex(
            name: "IX_ConsumableTransactions_SiteName",
            table: "ConsumableTransactions");

        migrationBuilder.DropColumn(
            name: "SiteId",
            table: "ConsumableTransactions");

        migrationBuilder.DropColumn(
            name: "SiteName",
            table: "ConsumableTransactions");
    }
}
