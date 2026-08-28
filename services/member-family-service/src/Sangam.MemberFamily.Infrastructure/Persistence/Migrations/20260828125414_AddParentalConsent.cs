using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParentalConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parental_consent_attestation",
                table: "child_profiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "parental_consent_given_at",
                table: "child_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parental_consent_given_by",
                table: "child_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parental_consent_notice_version",
                table: "child_profiles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parental_consent_attestation",
                table: "child_profiles");

            migrationBuilder.DropColumn(
                name: "parental_consent_given_at",
                table: "child_profiles");

            migrationBuilder.DropColumn(
                name: "parental_consent_given_by",
                table: "child_profiles");

            migrationBuilder.DropColumn(
                name: "parental_consent_notice_version",
                table: "child_profiles");
        }
    }
}
