using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Boli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OccasionVenueAndCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                table: "boli_occasions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "venue",
                table: "boli_occasions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city_id",
                table: "boli_occasions");

            migrationBuilder.DropColumn(
                name: "venue",
                table: "boli_occasions");
        }
    }
}
