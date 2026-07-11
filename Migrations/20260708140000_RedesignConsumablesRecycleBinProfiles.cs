using System;
using ITEquipmentInventory.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260708140000_RedesignConsumablesRecycleBinProfiles")]
public partial class RedesignConsumablesRecycleBinProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DateOfBirth",
            table: "AppUsers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProfileImagePath",
            table: "AppUsers",
            type: "TEXT",
            maxLength: 260,
            nullable: true);

        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS _ConsumablePrinterSeed (PrinterConsumableId INTEGER NOT NULL, PrinterName TEXT NOT NULL);");
        migrationBuilder.Sql(@"
            INSERT INTO _ConsumablePrinterSeed (PrinterConsumableId, PrinterName)
            SELECT pc.Id,
                   CASE
                       WHEN e.Name IS NOT NULL AND trim(e.Name) <> '' THEN e.Name
                       WHEN e.SerialNumber IS NOT NULL AND trim(e.SerialNumber) <> '' THEN 'Printer ' || e.SerialNumber
                       ELSE 'Nepoznati printer'
                   END
            FROM PrinterConsumables pc
            LEFT JOIN Equipment e ON e.Id = pc.EquipmentId;");

        migrationBuilder.Sql(@"
            CREATE TABLE _PrinterConsumables_new (
                Id INTEGER NOT NULL CONSTRAINT PK_PrinterConsumables PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ProductCode TEXT NULL,
                Type TEXT NOT NULL,
                Color TEXT NOT NULL,
                QuantityAvailable INTEGER NOT NULL,
                QuantityOrdered INTEGER NOT NULL,
                IsOriginal INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL
            );

            INSERT INTO _PrinterConsumables_new
                (Id, Name, ProductCode, Type, Color, QuantityAvailable, QuantityOrdered, IsOriginal, CreatedAt, UpdatedAt)
            SELECT Id, Name, ProductCode,
                   CASE WHEN Type = 'Bubanj' THEN 'Ostalo' ELSE Type END,
                   Color, QuantityAvailable, QuantityOrdered, IsOriginal, CreatedAt, UpdatedAt
            FROM PrinterConsumables;

            DROP TABLE PrinterConsumables;
            ALTER TABLE _PrinterConsumables_new RENAME TO PrinterConsumables;
            CREATE INDEX IX_PrinterConsumables_Name ON PrinterConsumables (Name);
            CREATE INDEX IX_PrinterConsumables_ProductCode ON PrinterConsumables (ProductCode);
        ");

        migrationBuilder.CreateTable(
            name: "ConsumableCompatiblePrinters",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                PrinterConsumableId = table.Column<int>(type: "INTEGER", nullable: false),
                PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConsumableCompatiblePrinters", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConsumableCompatiblePrinters_PrinterConsumables_PrinterConsumableId",
                    column: x => x.PrinterConsumableId,
                    principalTable: "PrinterConsumables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ConsumableTransactions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                PrinterConsumableId = table.Column<int>(type: "INTEGER", nullable: true),
                ConsumableName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                ProductCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ConsumableType = table.Column<string>(type: "TEXT", nullable: false),
                Color = table.Column<string>(type: "TEXT", nullable: false),
                TransactionType = table.Column<string>(type: "TEXT", nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                PerformedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConsumableTransactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConsumableTransactions_PrinterConsumables_PrinterConsumableId",
                    column: x => x.PrinterConsumableId,
                    principalTable: "PrinterConsumables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "DeletedItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                EntityLabel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                OriginalId = table.Column<int>(type: "INTEGER", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                DeletedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeletedItems", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ConsumableCompatiblePrinters_PrinterConsumableId_PrinterName",
            table: "ConsumableCompatiblePrinters",
            columns: new[] { "PrinterConsumableId", "PrinterName" },
            unique: true);

        migrationBuilder.CreateIndex(name: "IX_ConsumableTransactions_CreatedAt", table: "ConsumableTransactions", column: "CreatedAt");
        migrationBuilder.CreateIndex(name: "IX_ConsumableTransactions_PrinterConsumableId", table: "ConsumableTransactions", column: "PrinterConsumableId");
        migrationBuilder.CreateIndex(name: "IX_ConsumableTransactions_PrinterName", table: "ConsumableTransactions", column: "PrinterName");
        migrationBuilder.CreateIndex(name: "IX_DeletedItems_DeletedAtUtc", table: "DeletedItems", column: "DeletedAtUtc");
        migrationBuilder.CreateIndex(name: "IX_DeletedItems_ExpiresAtUtc", table: "DeletedItems", column: "ExpiresAtUtc");
        migrationBuilder.CreateIndex(name: "IX_DeletedItems_EntityType_OriginalId", table: "DeletedItems", columns: new[] { "EntityType", "OriginalId" });

        migrationBuilder.Sql(@"
            INSERT OR IGNORE INTO ConsumableCompatiblePrinters (PrinterConsumableId, PrinterName)
            SELECT PrinterConsumableId, PrinterName FROM _ConsumablePrinterSeed;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS _ConsumablePrinterSeed;");
        migrationBuilder.Sql("UPDATE PrinterConsumables SET Type = 'Ostalo' WHERE Type = 'Bubanj';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConsumableCompatiblePrinters");
        migrationBuilder.DropTable(name: "ConsumableTransactions");
        migrationBuilder.DropTable(name: "DeletedItems");

        migrationBuilder.AddColumn<int>(name: "EquipmentId", table: "PrinterConsumables", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "PrinterConsumables", type: "INTEGER", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<int>(name: "MinimumQuantity", table: "PrinterConsumables", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "Notes", table: "PrinterConsumables", type: "TEXT", maxLength: 1000, nullable: true);

        migrationBuilder.DropColumn(name: "DateOfBirth", table: "AppUsers");
        migrationBuilder.DropColumn(name: "ProfileImagePath", table: "AppUsers");
    }
}
