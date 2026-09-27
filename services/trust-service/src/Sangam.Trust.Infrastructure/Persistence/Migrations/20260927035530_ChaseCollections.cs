using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Trust.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChaseCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reminded_at",
                table: "trust_collections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_name",
                table: "trust_collections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Every pledge still outstanding is chased from now on, so one
            // recorded before this gets its Collection here - owed by its
            // member donor, or named by its donor name when there is none.
            // No "due" event is raised for these: they fell due when they were
            // pledged, and the audit trail already has that. The unique index
            // on (source_type, source_id) makes this safe to reason about - a
            // pledge can only ever have one.
            migrationBuilder.Sql("""
                INSERT INTO trust_collections
                    (id, tenant_id, trust_id, source_type, source_id, payer_member_id, payer_name,
                     amount, status, created_at)
                SELECT gen_random_uuid(), d.tenant_id, d.trust_id, 'Donation', d.id, d.donor_member_id,
                       CASE WHEN d.donor_member_id IS NULL THEN d.donor_name END,
                       d.amount, 'Due', d.created_at
                FROM trust_donations d
                WHERE d.status = 'Pledged'
                  AND NOT EXISTS (
                      SELECT 1 FROM trust_collections c
                      WHERE c.source_type = 'Donation' AND c.source_id = d.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_reminded_at",
                table: "trust_collections");

            migrationBuilder.DropColumn(
                name: "payer_name",
                table: "trust_collections");
        }
    }
}
