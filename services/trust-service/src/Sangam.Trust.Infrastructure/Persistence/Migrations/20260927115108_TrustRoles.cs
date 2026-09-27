using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Trust.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrustRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: free-text position titles become the Trust's own
            // roles, and every row is carried across before the old columns
            // go. TrustRolesMigrationTests runs this against legacy rows.
            migrationBuilder.CreateTable(
                name: "trust_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_trust_roles_trusts_trust_id",
                        column: x => x.trust_id,
                        principalTable: "trusts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "trust_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "trust_elections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Every election before this filled one seat.
            migrationBuilder.AddColumn<int>(
                name: "seats",
                table: "trust_elections",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "winner_member_ids",
                table: "trust_elections",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            // And every ballot before this had one pick.
            migrationBuilder.AddColumn<int>(
                name: "choice",
                table: "trust_election_votes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Trustee, on every Trust.
            migrationBuilder.Sql("""
                INSERT INTO trust_roles (id, trust_id, title, kind, seats, created_at)
                SELECT gen_random_uuid(), t.id, 'Trustee', 'Trustee', NULL, t.created_at
                FROM trusts t;
                """);

            // One role per distinct title a Trust used, on its roster or for an
            // election, sized to hold everybody active who carries it now.
            migrationBuilder.Sql("""
                INSERT INTO trust_roles (id, trust_id, title, kind, seats, created_at)
                SELECT gen_random_uuid(), used.trust_id, min(used.title), 'Elected',
                       greatest(1, sum(used.holding))::int, now()
                FROM (
                    SELECT m.trust_id, left(trim(m.position_title), 100) AS title,
                           CASE WHEN m.status = 'Active' THEN 1 ELSE 0 END AS holding
                    FROM trust_members m
                    WHERE m.position_title IS NOT NULL AND trim(m.position_title) <> ''
                    UNION ALL
                    SELECT e.trust_id, left(trim(e.position_title), 100), 0
                    FROM trust_elections e
                    WHERE trim(e.position_title) <> ''
                ) used
                WHERE lower(used.title) <> 'trustee'
                GROUP BY used.trust_id, lower(used.title);
                """);

            migrationBuilder.Sql("""
                UPDATE trust_members m SET role_id = r.id
                FROM trust_roles r
                WHERE r.trust_id = m.trust_id
                  AND m.position_title IS NOT NULL
                  AND lower(r.title) = lower(left(trim(m.position_title), 100));

                UPDATE trust_members m SET role_id = r.id
                FROM trust_roles r
                WHERE m.role_id = '00000000-0000-0000-0000-000000000000'
                  AND r.trust_id = m.trust_id AND r.kind = 'Trustee';

                UPDATE trust_elections e SET role_id = r.id
                FROM trust_roles r
                WHERE r.trust_id = e.trust_id
                  AND lower(r.title) = lower(left(trim(e.position_title), 100));

                UPDATE trust_elections SET winner_member_ids = ARRAY[winner_member_id]
                WHERE winner_member_id IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id",
                table: "trust_election_votes");

            migrationBuilder.DropColumn(
                name: "position_title",
                table: "trust_members");

            migrationBuilder.DropColumn(
                name: "position_title",
                table: "trust_elections");

            migrationBuilder.DropColumn(
                name: "winner_member_id",
                table: "trust_elections");

            migrationBuilder.CreateIndex(
                name: "ix_trust_members_trust_id_role_id",
                table: "trust_members",
                columns: new[] { "trust_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trust_elections_role_id",
                table: "trust_elections",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id_candidate_",
                table: "trust_election_votes",
                columns: new[] { "election_id", "voter_member_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id_choice",
                table: "trust_election_votes",
                columns: new[] { "election_id", "voter_member_id", "choice" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_roles_trust_id",
                table: "trust_roles",
                column: "trust_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trust_roles");

            migrationBuilder.DropIndex(
                name: "ix_trust_members_trust_id_role_id",
                table: "trust_members");

            migrationBuilder.DropIndex(
                name: "ix_trust_elections_role_id",
                table: "trust_elections");

            migrationBuilder.DropIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id_candidate_",
                table: "trust_election_votes");

            migrationBuilder.DropIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id_choice",
                table: "trust_election_votes");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "trust_members");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "trust_elections");

            migrationBuilder.DropColumn(
                name: "seats",
                table: "trust_elections");

            migrationBuilder.DropColumn(
                name: "winner_member_ids",
                table: "trust_elections");

            migrationBuilder.DropColumn(
                name: "choice",
                table: "trust_election_votes");

            migrationBuilder.AddColumn<string>(
                name: "position_title",
                table: "trust_members",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "position_title",
                table: "trust_elections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "winner_member_id",
                table: "trust_elections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_election_votes_election_id_voter_member_id",
                table: "trust_election_votes",
                columns: new[] { "election_id", "voter_member_id" },
                unique: true);
        }
    }
}
