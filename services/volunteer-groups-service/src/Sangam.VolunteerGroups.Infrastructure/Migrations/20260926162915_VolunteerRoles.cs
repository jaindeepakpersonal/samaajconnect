using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.VolunteerGroups.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VolunteerRoles : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Hand-ordered, not as scaffolded: the scaffold dropped
        /// <c>role_position</c> and <c>winner_member_id</c> before anything
        /// could read them. Positions typed on existing groups become defined
        /// roles (confirmed with the user): every group gets Volunteer and
        /// President, plus one Elected role per distinct title its members held
        /// or its elections ran for, with as many seats as people held it.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_election_votes_election_id_voter_member_id",
                table: "election_votes");

            // ---- New columns, nullable or defaulted until filled -------------

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "group_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "group_elections",
                type: "uuid",
                nullable: true);

            // Every election before this one filled one position.
            migrationBuilder.AddColumn<int>(
                name: "seats",
                table: "group_elections",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "winner_member_ids",
                table: "group_elections",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            // Every vote before this one was its voter's only pick.
            migrationBuilder.AddColumn<int>(
                name: "choice",
                table: "election_votes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "group_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_roles_volunteer_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "volunteer_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Roles out of what was typed ---------------------------------

            migrationBuilder.Sql("""
                INSERT INTO group_roles (id, group_id, title, kind, seats, created_at)
                SELECT gen_random_uuid(), g.id, 'Volunteer', 'Volunteer', NULL, g.created_at
                FROM volunteer_groups g;

                INSERT INTO group_roles (id, group_id, title, kind, seats, created_at)
                SELECT gen_random_uuid(), g.id, 'President', 'President', 1, g.created_at
                FROM volunteer_groups g;

                -- One Elected role per distinct title (ignoring case and spaces)
                -- a member held or an election ran for, other than the two
                -- built-in ones. Seats: however many held it, at least one.
                INSERT INTO group_roles (id, group_id, title, kind, seats, created_at)
                SELECT gen_random_uuid(), used.group_id, min(used.title), 'Elected',
                       greatest(1, sum(used.holders))::int, now()
                FROM (
                    SELECT group_id, btrim(role_position) AS title, count(*) AS holders
                    FROM group_members
                    WHERE role_position IS NOT NULL AND btrim(role_position) <> ''
                    GROUP BY group_id, btrim(role_position)
                    UNION ALL
                    SELECT group_id, btrim(position_title), 0
                    FROM group_elections
                    GROUP BY group_id, btrim(position_title)
                ) used
                WHERE lower(used.title) NOT IN ('volunteer', 'president')
                GROUP BY used.group_id, lower(used.title);
                """);

            // ---- Members into roles ------------------------------------------

            migrationBuilder.Sql("""
                -- The typed title, where it names a role.
                UPDATE group_members m
                SET role_id = r.id
                FROM group_roles r
                WHERE r.group_id = m.group_id
                  AND r.kind = 'Elected'
                  AND m.role_position IS NOT NULL
                  AND lower(r.title) = lower(btrim(m.role_position));

                -- The actual president holds President, whatever their label said.
                UPDATE group_members m
                SET role_id = r.id
                FROM volunteer_groups g
                JOIN group_roles r ON r.group_id = g.id AND r.kind = 'President'
                WHERE g.id = m.group_id AND g.president_member_id = m.member_id;

                -- Everybody else - no title, or labelled "President" without
                -- being it - is a Volunteer.
                UPDATE group_members m
                SET role_id = r.id
                FROM group_roles r
                WHERE r.group_id = m.group_id AND r.kind = 'Volunteer' AND m.role_id IS NULL;
                """);

            // ---- Elections onto their role -----------------------------------

            migrationBuilder.Sql("""
                UPDATE group_elections e
                SET role_id = r.id
                FROM group_roles r
                WHERE r.group_id = e.group_id
                  AND lower(r.title) = lower(btrim(e.position_title));

                -- Only an election titled "Volunteer" can be left over; history
                -- only, since Volunteer has no elections from here on.
                UPDATE group_elections e
                SET role_id = r.id
                FROM group_roles r
                WHERE r.group_id = e.group_id AND r.kind = 'Volunteer' AND e.role_id IS NULL;

                UPDATE group_elections
                SET winner_member_ids = ARRAY[winner_member_id]
                WHERE winner_member_id IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "group_members",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "group_elections",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "role_position",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "winner_member_id",
                table: "group_elections");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_role_id",
                table: "group_members",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_elections_group_id_role_id",
                table: "group_elections",
                columns: new[] { "group_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_election_votes_election_id_voter_member_id_candidate_id",
                table: "election_votes",
                columns: new[] { "election_id", "voter_member_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_election_votes_election_id_voter_member_id_choice",
                table: "election_votes",
                columns: new[] { "election_id", "voter_member_id", "choice" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_roles_group_id_title",
                table: "group_roles",
                columns: new[] { "group_id", "title" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_group_members_group_role_role_id",
                table: "group_members",
                column: "role_id",
                principalTable: "group_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_group_members_group_role_role_id",
                table: "group_members");

            migrationBuilder.DropTable(
                name: "group_roles");

            migrationBuilder.DropIndex(
                name: "ix_group_members_role_id",
                table: "group_members");

            migrationBuilder.DropIndex(
                name: "ix_group_elections_group_id_role_id",
                table: "group_elections");

            migrationBuilder.DropIndex(
                name: "ix_election_votes_election_id_voter_member_id_candidate_id",
                table: "election_votes");

            migrationBuilder.DropIndex(
                name: "ix_election_votes_election_id_voter_member_id_choice",
                table: "election_votes");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "group_elections");

            migrationBuilder.DropColumn(
                name: "seats",
                table: "group_elections");

            migrationBuilder.DropColumn(
                name: "winner_member_ids",
                table: "group_elections");

            migrationBuilder.DropColumn(
                name: "choice",
                table: "election_votes");

            migrationBuilder.AddColumn<string>(
                name: "role_position",
                table: "group_members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "winner_member_id",
                table: "group_elections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_election_votes_election_id_voter_member_id",
                table: "election_votes",
                columns: new[] { "election_id", "voter_member_id" },
                unique: true);
        }
    }
}
