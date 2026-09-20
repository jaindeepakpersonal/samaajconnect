using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Panch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ballots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    election_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ballots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "election_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    election_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ranked_candidate_ids = table.Column<string>(type: "jsonb", nullable: false),
                    seated_member_ids = table.Column<string>(type: "jsonb", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_election_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "panch_elections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    nomination_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nomination_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voting_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voting_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    results_visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panch_elections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "panel_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    election_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_panel_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    election_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_votes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    election_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nominated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    nominated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    panch_election_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidates_elections_panch_election_id",
                        column: x => x.panch_election_id,
                        principalTable: "panch_elections",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_candidates_panch_elections_election_id",
                        column: x => x.election_id,
                        principalTable: "panch_elections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ballots_election_id_voter_member_id",
                table: "ballots",
                columns: new[] { "election_id", "voter_member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidates_election_id_member_id",
                table: "candidates",
                columns: new[] { "election_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidates_panch_election_id",
                table: "candidates",
                column: "panch_election_id");

            migrationBuilder.CreateIndex(
                name: "ix_election_results_election_id",
                table: "election_results",
                column: "election_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_panch_elections_tenant_id_status",
                table: "panch_elections",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_panel_members_tenant_id_member_id",
                table: "panel_members",
                columns: new[] { "tenant_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_votes_election_id_candidate_id",
                table: "votes",
                columns: new[] { "election_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_votes_election_id_voter_member_id_candidate_id",
                table: "votes",
                columns: new[] { "election_id", "voter_member_id", "candidate_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ballots");

            migrationBuilder.DropTable(
                name: "candidates");

            migrationBuilder.DropTable(
                name: "election_results");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "panel_members");

            migrationBuilder.DropTable(
                name: "votes");

            migrationBuilder.DropTable(
                name: "panch_elections");
        }
    }
}
