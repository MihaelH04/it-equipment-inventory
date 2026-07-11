using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentEmployeeToEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarrantyUntil",
                table: "Equipment");

            migrationBuilder.AddColumn<int>(
                name: "CurrentEmployeeId",
                table: "Equipment",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_CurrentEmployeeId",
                table: "Equipment",
                column: "CurrentEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Equipment_Employees_CurrentEmployeeId",
                table: "Equipment",
                column: "CurrentEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Equipment_Employees_CurrentEmployeeId",
                table: "Equipment");

            migrationBuilder.DropIndex(
                name: "IX_Equipment_CurrentEmployeeId",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "CurrentEmployeeId",
                table: "Equipment");

            migrationBuilder.AddColumn<DateTime>(
                name: "WarrantyUntil",
                table: "Equipment",
                type: "TEXT",
                nullable: true);
        }
    }
}
