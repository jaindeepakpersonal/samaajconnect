using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Replaces a client-supplied logo link with one the platform hosts.
    /// </summary>
    /// <remarks>
    /// <c>logo_url</c> is dropped and nothing is migrated, and unlike the
    /// equivalent change in member-family-service that costs nothing at all:
    /// no command ever took a logo, so the column has been null on every row the
    /// platform has ever had. It was a field the API could read and nothing
    /// could write, beside an "Upload Logo" control the admin wireframe drew
    /// with nothing behind it.
    /// </remarks>
    public partial class TenantLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "tenants");

            migrationBuilder.AddColumn<Guid>(
                name: "logo_image_id",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_logos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_logos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_logos_tenant",
                table: "tenant_logos",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_logos");

            migrationBuilder.DropColumn(
                name: "logo_image_id",
                table: "tenants");

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "tenants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
}
