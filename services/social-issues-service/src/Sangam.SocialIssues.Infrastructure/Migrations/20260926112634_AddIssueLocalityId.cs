using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.SocialIssues.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueLocalityId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locality_id",
                table: "social_issues",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "locality_id",
                table: "social_issues");
        }
    }
}
