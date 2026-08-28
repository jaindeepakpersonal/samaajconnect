using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrievanceContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "grievance_contact_email",
                table: "tenants",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grievance_contact_name",
                table: "tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grievance_contact_phone",
                table: "tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "grievance_contact_email",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "grievance_contact_name",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "grievance_contact_phone",
                table: "tenants");
        }
    }
}
