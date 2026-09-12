using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Matrimony.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "biodata_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gautra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profession_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qualification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    locality_id = table.Column<Guid>(type: "uuid", nullable: true),
                    height_cm = table.Column<int>(type: "integer", nullable: true),
                    marital_status_id = table.Column<Guid>(type: "uuid", nullable: true),
                    diet = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    family_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    father_occupation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mother_occupation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    siblings_count = table.Column<int>(type: "integer", nullable: true),
                    manglik = table.Column<bool>(type: "boolean", nullable: true),
                    horoscope_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    about_me = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    preferred_age_min = table.Column<int>(type: "integer", nullable: true),
                    preferred_age_max = table.Column<int>(type: "integer", nullable: true),
                    partner_preference_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consent_notice_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consent_attestation = table.Column<string>(type: "text", nullable: false),
                    consent_given_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consent_withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biodata_profiles", x => x.id);
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
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_biodata_profiles_tenant_id_is_active",
                table: "biodata_profiles",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_tenant_id_from_member_id_to_member_id_status",
                table: "proposals",
                columns: new[] { "tenant_id", "from_member_id", "to_member_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_tenant_id_to_member_id_from_member_id_status",
                table: "proposals",
                columns: new[] { "tenant_id", "to_member_id", "from_member_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "biodata_profiles");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "proposals");
        }
    }
}
