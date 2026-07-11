using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class PromejnjenSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Manager",
                table: "Sites");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Sites",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Sites");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Sites",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Manager",
                table: "Sites",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }
    }
}
