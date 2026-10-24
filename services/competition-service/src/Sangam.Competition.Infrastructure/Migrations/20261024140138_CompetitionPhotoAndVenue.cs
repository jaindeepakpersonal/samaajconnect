using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Competition.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPhotoAndVenue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "city_id",
                table: "competitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                table: "competitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "venue",
                table: "competitions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "competition_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_photos_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_photos_competition_id",
                table: "competition_photos",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_photos_tenant_id_competition_id",
                table: "competition_photos",
                columns: new[] { "tenant_id", "competition_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_photos");

            migrationBuilder.DropColumn(
                name: "city_id",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "venue",
                table: "competitions");
        }
    }
}
