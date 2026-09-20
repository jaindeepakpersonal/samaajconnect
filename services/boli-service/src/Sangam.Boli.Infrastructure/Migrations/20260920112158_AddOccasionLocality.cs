using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Boli.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOccasionLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locality_id",
                table: "boli_occasions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_boli_occasions_tenant_id_locality_id",
                table: "boli_occasions",
                columns: new[] { "tenant_id", "locality_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_boli_occasions_tenant_id_locality_id",
                table: "boli_occasions");

            migrationBuilder.DropColumn(
                name: "locality_id",
                table: "boli_occasions");
        }
    }
}
