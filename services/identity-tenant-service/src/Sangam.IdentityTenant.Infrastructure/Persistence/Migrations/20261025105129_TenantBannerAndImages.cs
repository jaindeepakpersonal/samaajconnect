using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A Samaaj banner beside its logo, and the logos table renamed to hold both.
    /// </summary>
    /// <remarks>
    /// Hand-edited: EF scaffolds an entity's table rename as drop-and-create,
    /// which would delete every Samaaj logo on the platform. These are renames,
    /// so every existing row - and its id, which <c>tenants.logo_image_id</c>
    /// points at - survives untouched.
    /// </remarks>
    public partial class TenantBannerAndImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "tenant_logos",
                newName: "tenant_images");

            migrationBuilder.Sql(
                "ALTER TABLE tenant_images RENAME CONSTRAINT pk_tenant_logos TO pk_tenant_images;");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_logos_tenant",
                table: "tenant_images",
                newName: "ix_tenant_images_tenant");

            migrationBuilder.AddColumn<Guid>(
                name: "banner_image_id",
                table: "tenants",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "banner_image_id",
                table: "tenants");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_images_tenant",
                table: "tenant_images",
                newName: "ix_tenant_logos_tenant");

            migrationBuilder.Sql(
                "ALTER TABLE tenant_images RENAME CONSTRAINT pk_tenant_images TO pk_tenant_logos;");

            migrationBuilder.RenameTable(
                name: "tenant_images",
                newName: "tenant_logos");
        }
    }
}
