using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.SocialIssues.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PanchJudgesIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "published_at",
                table: "social_issues",
                newName: "decided_at");

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "social_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "social_issues",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "monitoring_group_id",
                table: "social_issues",
                type: "uuid",
                nullable: true);

            // ---------------------------------------------------------------
            // The workflow lost two states, so the rows that were in them move.
            //
            // Approved -> DecisionPending. An issue a reviewer had accepted and
            // not yet published is exactly what "in front of the Panch, awaiting
            // its decision" now means.
            //
            // Published -> Closed. A published issue had been dealt with as far
            // as the old workflow went. Its published_at was renamed to
            // decided_at above, so it keeps the moment it became visible and
            // therefore stays visible to the Samaaj (a closed issue is public
            // exactly when it has a decided_at); it simply carries no decision
            // text, because nobody ever wrote one.
            //
            // The history holds the same two names, and the enum can no longer
            // read them, so those rows move too - otherwise the first request for
            // an old issue's history would fail to parse.
            // ---------------------------------------------------------------
            migrationBuilder.Sql("UPDATE social_issues SET status = 'DecisionPending' WHERE status = 'Approved';");
            migrationBuilder.Sql("UPDATE social_issues SET status = 'Closed' WHERE status = 'Published';");
            migrationBuilder.Sql("UPDATE issue_status_history SET from_status = 'DecisionPending' WHERE from_status = 'Approved';");
            migrationBuilder.Sql("UPDATE issue_status_history SET to_status = 'DecisionPending' WHERE to_status = 'Approved';");
            migrationBuilder.Sql("UPDATE issue_status_history SET from_status = 'Closed' WHERE from_status = 'Published';");
            migrationBuilder.Sql("UPDATE issue_status_history SET to_status = 'Closed' WHERE to_status = 'Published';");

            migrationBuilder.CreateTable(
                name: "panel_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panel_members", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_panel_members_tenant_id_member_id",
                table: "panel_members",
                columns: new[] { "tenant_id", "member_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best effort: an issue in front of the Panch goes back to Approved.
            // A Closed issue cannot be told apart from one that was Published
            // and one that was withdrawn, so it stays Closed.
            migrationBuilder.Sql("UPDATE social_issues SET status = 'Approved' WHERE status = 'DecisionPending';");
            migrationBuilder.Sql("UPDATE issue_status_history SET from_status = 'Approved' WHERE from_status = 'DecisionPending';");
            migrationBuilder.Sql("UPDATE issue_status_history SET to_status = 'Approved' WHERE to_status = 'DecisionPending';");
            migrationBuilder.DropTable(
                name: "panel_members");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "social_issues");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "social_issues");

            migrationBuilder.DropColumn(
                name: "monitoring_group_id",
                table: "social_issues");

            migrationBuilder.RenameColumn(
                name: "decided_at",
                table: "social_issues",
                newName: "published_at");
        }
    }
}
