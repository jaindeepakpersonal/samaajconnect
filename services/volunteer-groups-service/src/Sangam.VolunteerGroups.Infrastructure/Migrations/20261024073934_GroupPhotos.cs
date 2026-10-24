using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.VolunteerGroups.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GroupPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                table: "volunteer_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_photos_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "volunteer_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_photos_group_id",
                table: "group_photos",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_photos_tenant_id_group_id",
                table: "group_photos",
                columns: new[] { "tenant_id", "group_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_photos");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "volunteer_groups");
        }
    }
}
