using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.AuditNotification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_source_message_id",
                table: "notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivery_attempts",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_claim_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination",
                table: "notifications",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_delivery_claim_id",
                table: "notifications",
                column: "delivery_claim_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_message_id_channel",
                table: "notifications",
                columns: new[] { "source_message_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_created_at",
                table: "notifications",
                columns: new[] { "status", "created_at" });

            // Every row that exists when this runs is in-app, and an in-app
            // notification is delivered by being written - so its delivered_at
            // is its created_at. Backfilled rather than left null, because a
            // column that is empty for older rows and set for newer ones is one
            // somebody eventually reads as "delivery stopped working in
            // September".
            migrationBuilder.Sql(
                """
                UPDATE notifications
                SET delivered_at = created_at
                WHERE channel = 'InApp' AND status IN ('Sent', 'Read');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_delivery_claim_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_source_message_id_channel",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_status_created_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "delivery_attempts",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "delivery_claim_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "destination",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_message_id",
                table: "notifications",
                column: "source_message_id",
                unique: true);
        }
    }
}
