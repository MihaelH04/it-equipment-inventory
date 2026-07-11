using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITEquipmentInventory.Migrations
{
    /// <inheritdoc />
    public partial class FixDesktopRacunaloToPC : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Equipment
                SET EquipmentType = 'PC'
                WHERE EquipmentType = 'DesktopRacunalo';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Equipment
                SET EquipmentType = 'DesktopRacunalo'
                WHERE EquipmentType = 'PC';
            ");
        }
    }
}