using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Boli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBoliAutoExtend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_extend_seconds",
                table: "boli",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_extend_seconds",
                table: "boli");
        }
    }
}
