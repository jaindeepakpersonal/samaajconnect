using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.VolunteerGroups.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locality_id",
                table: "volunteer_groups",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "locality_id",
                table: "volunteer_groups");
        }
    }
}
