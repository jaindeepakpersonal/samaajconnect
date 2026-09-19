using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FamilyAdminAndHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: EF's diff saw the old column disappear and a new one
            // appear and generated an alter plus an add, which would have given
            // every existing household an empty admin id. What was called the
            // family head has always been the person who manages the household,
            // so the column is renamed with its values, and the new (nullable)
            // family head - the eldest member, chosen by the admin - starts empty.
            migrationBuilder.RenameColumn(
                name: "family_head_member_id",
                table: "families",
                newName: "family_admin_member_id");

            migrationBuilder.AddColumn<Guid>(
                name: "family_head_member_id",
                table: "families",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "family_head_member_id",
                table: "families");

            migrationBuilder.RenameColumn(
                name: "family_admin_member_id",
                table: "families",
                newName: "family_head_member_id");
        }
    }
}
