using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGautraQualificationAndStructuredAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_member_profiles_tenant_id_locality",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "locality",
                table: "member_profiles");

            // Best-effort preservation: the old free-text address becomes the
            // permanent address's line, matching the field it always meant -
            // "your address" was never the supplementary/temporary one. The
            // Locality column dropped just above has no such rename, because
            // there is no way to map free text onto a master-data-service id;
            // LocalityId stays null for every migrated row until the member
            // picks one from the new dropdown.
            migrationBuilder.RenameColumn(
                name: "address",
                table: "member_profiles",
                newName: "permanent_address_line");

            migrationBuilder.AddColumn<Guid>(
                name: "gautra_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "permanent_address_city_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "permanent_address_locality_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "temporary_address_line",
                table: "member_profiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "temporary_address_city_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "temporary_address_locality_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "qualification_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);

            // Defaulted to SamaajOnly, the same platform default
            // FieldPrivacy.Default.Qualification uses - not an empty string,
            // which HasConversion<string>() cannot parse back into a
            // PrivacyLevel on the very next read of an existing row.
            migrationBuilder.AddColumn<string>(
                name: "privacy_qualification",
                table: "member_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SamaajOnly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gautra_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "permanent_address_city_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "permanent_address_locality_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "temporary_address_line",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "temporary_address_city_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "temporary_address_locality_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "qualification_id",
                table: "member_profiles");

            migrationBuilder.DropColumn(
                name: "privacy_qualification",
                table: "member_profiles");

            migrationBuilder.RenameColumn(
                name: "permanent_address_line",
                table: "member_profiles",
                newName: "address");

            migrationBuilder.AddColumn<string>(
                name: "locality",
                table: "member_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_profiles_tenant_id_locality",
                table: "member_profiles",
                columns: new[] { "tenant_id", "locality" });
        }
    }
}
