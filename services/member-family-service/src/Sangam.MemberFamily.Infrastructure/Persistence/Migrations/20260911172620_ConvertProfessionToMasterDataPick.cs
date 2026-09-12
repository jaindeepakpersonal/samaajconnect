using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertProfessionToMasterDataPick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Free text cannot be resolved into a master-data-service
            // ProfessionType id with any confidence - there is no lookup from
            // "Architect" typed by hand to a specific LookupItem row, the same
            // reason PermanentAddress.LocalityId could not be back-filled from
            // old free-text addresses in the migration that added it. Existing
            // values are dropped rather than carried forward; every migrated
            // row's ProfessionId starts null, same as any other field nobody
            // has picked from the new dropdown yet.
            migrationBuilder.DropColumn(
                name: "profession",
                table: "member_profiles");

            migrationBuilder.AddColumn<Guid>(
                name: "profession_id",
                table: "member_profiles",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "profession_id",
                table: "member_profiles");

            migrationBuilder.AddColumn<string>(
                name: "profession",
                table: "member_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }
    }
}
