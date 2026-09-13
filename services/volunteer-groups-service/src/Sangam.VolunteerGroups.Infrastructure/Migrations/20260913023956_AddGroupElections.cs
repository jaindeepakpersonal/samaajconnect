using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.VolunteerGroups.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupElections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "election_votes",
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
                    table.PrimaryKey("pk_election_votes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_elections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_group_elections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "election_candidates",
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
                    group_election_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_election_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_election_candidates_elections_group_election_id",
                        column: x => x.group_election_id,
                        principalTable: "group_elections",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_election_candidates_group_elections_election_id",
                        column: x => x.election_id,
                        principalTable: "group_elections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_election_candidates_election_id_member_id",
                table: "election_candidates",
                columns: new[] { "election_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_election_candidates_group_election_id",
                table: "election_candidates",
                column: "group_election_id");

            migrationBuilder.CreateIndex(
                name: "ix_election_votes_election_id_candidate_id",
                table: "election_votes",
                columns: new[] { "election_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_election_votes_election_id_voter_member_id",
                table: "election_votes",
                columns: new[] { "election_id", "voter_member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_elections_group_id_status",
                table: "group_elections",
                columns: new[] { "group_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "election_candidates");

            migrationBuilder.DropTable(
                name: "election_votes");

            migrationBuilder.DropTable(
                name: "group_elections");
        }
    }
}
