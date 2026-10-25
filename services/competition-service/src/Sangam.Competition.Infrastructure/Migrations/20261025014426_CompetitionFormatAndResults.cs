using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Competition.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionFormatAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "rounds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Main");

            migrationBuilder.AddColumn<DateOnly>(
                name: "audition_date",
                table: "competitions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "audition_required",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "finalist_count",
                table: "competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_marks",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "round_count",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "panel",
                table: "competition_judges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Main");

            migrationBuilder.CreateTable(
                name: "competition_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ranked_member_ids = table.Column<string>(type: "jsonb", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_results_competition_id",
                table: "competition_results",
                column: "competition_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_results");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "rounds");

            migrationBuilder.DropColumn(
                name: "audition_date",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "audition_required",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "finalist_count",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "max_marks",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "round_count",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "panel",
                table: "competition_judges");
        }
    }
}
