using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "event_type_id",
                table: "events",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "event_type_id",
                table: "events");
        }
    }
}
