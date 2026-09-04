using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WithdrawParentalConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "parental_consent_withdrawn_at",
                table: "child_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parental_consent_withdrawn_by_member_id",
                table: "child_profiles",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parental_consent_withdrawn_at",
                table: "child_profiles");

            migrationBuilder.DropColumn(
                name: "parental_consent_withdrawn_by_member_id",
                table: "child_profiles");
        }
    }
}
