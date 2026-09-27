using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Competition.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locality_id",
                table: "competitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_competitions_tenant_id_locality_id",
                table: "competitions",
                columns: new[] { "tenant_id", "locality_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_competitions_tenant_id_locality_id",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "locality_id",
                table: "competitions");
        }
    }
}
