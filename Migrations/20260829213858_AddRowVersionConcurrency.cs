using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PrinterConsumables",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Equipment",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Employees",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.Sql("UPDATE PrinterConsumables SET RowVersion = randomblob(16);");
            migrationBuilder.Sql("UPDATE Equipment SET RowVersion = randomblob(16);");
            migrationBuilder.Sql("UPDATE Employees SET RowVersion = randomblob(16);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PrinterConsumables");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Employees");
        }
    }
}
