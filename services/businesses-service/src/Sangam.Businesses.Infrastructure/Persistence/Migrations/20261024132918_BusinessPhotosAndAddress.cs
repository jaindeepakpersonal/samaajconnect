using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Businesses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusinessPhotosAndAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_line",
                table: "businesses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                table: "businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                table: "business_products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "business_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_images_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_images_business_id",
                table: "business_images",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_images_tenant_id_business_id",
                table: "business_images",
                columns: new[] { "tenant_id", "business_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_images");

            migrationBuilder.DropColumn(
                name: "address_line",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "business_products");
        }
    }
}
