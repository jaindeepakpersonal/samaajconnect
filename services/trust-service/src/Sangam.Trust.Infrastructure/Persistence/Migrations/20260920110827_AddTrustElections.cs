using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Trust.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustElections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trust_election_votes",
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
                    table.PrimaryKey("pk_trust_election_votes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trust_elections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nomination_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nomination_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voting_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voting_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    winner_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_elections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trust_election_candidates",
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
                    trust_election_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_election_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_trust_election_candidates_trust_elections_election_id",
                        column: x => x.election_id,
                        principalTable: "trust_elections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trust_election_candidates_trust_elections_trust_election_id",
                        column: x => x.trust_election_id,
                        principalTable: "trust_elections",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_candidates_election_id_member_id",
                table: "trust_election_candidates",
                columns: new[] { "election_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_candidates_trust_election_id",
                table: "trust_election_candidates",
                column: "trust_election_id");

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_votes_election_id_candidate_id",
                table: "trust_election_votes",
                columns: new[] { "election_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id",
                table: "trust_election_votes",
                columns: new[] { "election_id", "voter_member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_elections_trust_id_status",
                table: "trust_elections",
                columns: new[] { "trust_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trust_election_candidates");

            migrationBuilder.DropTable(
                name: "trust_election_votes");

            migrationBuilder.DropTable(
                name: "trust_elections");
        }
    }
}
