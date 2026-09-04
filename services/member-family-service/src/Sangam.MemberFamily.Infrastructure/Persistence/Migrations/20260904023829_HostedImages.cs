using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Replaces client-supplied photo links with images the platform hosts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two photo_url columns are dropped, and their contents are not
    /// migrated.</b> That is a deliberate loss and worth stating plainly rather
    /// than leaving somebody to infer it from a DropColumn.
    /// </para>
    /// <para>
    /// There is no honest way to carry those values across. Turning a URL into a
    /// hosted image means fetching it, and a migration that fetches every
    /// client-supplied address it finds in a database is a server-side request
    /// forgery with a schema change wrapped around it: the addresses were
    /// supplied by users, they are only validated as absolute http(s), and the
    /// fetch would come from inside the network. Keeping the column instead
    /// would leave the platform holding the exact third-party links this change
    /// exists to stop using.
    /// </para>
    /// <para>
    /// So the photos are cleared and members re-upload. On a platform that has
    /// not gone live that costs nothing; after it has, this migration should be
    /// paired with a notice telling members their photo needs setting again.
    /// </para>
    /// </remarks>
    public partial class HostedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // See the remarks above: not migrated, deliberately.
            migrationBuilder.DropColumn(
                name: "photo_url",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "photo_url",
                table: "child_profiles");

            migrationBuilder.AddColumn<Guid>(
                name: "photo_image_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_image_id",
                table: "child_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stored_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_images", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_images_owner",
                table: "stored_images",
                columns: new[] { "tenant_id", "owner_kind", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_images");

            migrationBuilder.DropColumn(
                name: "photo_image_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "photo_image_id",
                table: "child_profiles");

            migrationBuilder.AddColumn<string>(
                name: "photo_url",
                table: "member_profiles",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_url",
                table: "child_profiles",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
}
