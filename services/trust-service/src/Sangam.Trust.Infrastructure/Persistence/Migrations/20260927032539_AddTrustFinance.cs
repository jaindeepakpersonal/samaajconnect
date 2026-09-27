using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Trust.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trust_collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    collected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_collections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trust_donations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    donor_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receipt_number = table.Column<int>(type: "integer", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    collected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_donations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trust_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trust_expenses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trust_collections_payer_member_id",
                table: "trust_collections",
                column: "payer_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_trust_collections_source_type_source_id",
                table: "trust_collections",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trust_collections_trust_id_created_at",
                table: "trust_collections",
                columns: new[] { "trust_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trust_donations_trust_id_created_at",
                table: "trust_donations",
                columns: new[] { "trust_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trust_donations_trust_id_receipt_number",
                table: "trust_donations",
                columns: new[] { "trust_id", "receipt_number" },
                unique: true,
                filter: "receipt_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trust_expenses_trust_id_recorded_at",
                table: "trust_expenses",
                columns: new[] { "trust_id", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trust_collections");

            migrationBuilder.DropTable(
                name: "trust_donations");

            migrationBuilder.DropTable(
                name: "trust_expenses");
        }
    }
}
