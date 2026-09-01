using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.AuditNotification.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves read state off the notification and onto a row per person.
    /// </summary>
    /// <remarks>
    /// The old shape could not express a broadcast: one row, no recipient, seen
    /// by a whole Samaaj - so the first member to open it would have marked it
    /// read for all of them. See NotificationRead.
    ///
    /// EF scaffolds this as a plain DropColumn and warns about data loss. The
    /// table is created and the existing state copied into it first, so nothing
    /// is lost. In practice there is nothing to copy: no command ever reached
    /// Notification.MarkRead, so no row can be Read. The backfill is here
    /// because "there cannot be any" is a claim about today's code, and a
    /// migration outlives the day it was written.
    /// </remarks>
    public partial class PerMemberReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_reads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_reads", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_reads_notifications_notification_id",
                        column: x => x.notification_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_reads_notification_id_user_id",
                table: "notification_reads",
                columns: new[] { "notification_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_reads_user_id",
                table: "notification_reads",
                column: "user_id");

            // Only rows with a recipient. A broadcast that had somehow been
            // marked read carried no record of who read it - that is the defect
            // this migration exists to fix - so there is nobody to attribute it
            // to and it comes back unread for everyone, which is the safe way to
            // be wrong about a message somebody may not have seen.
            migrationBuilder.Sql(
                """
                INSERT INTO notification_reads (id, notification_id, user_id, tenant_id, read_at)
                SELECT gen_random_uuid(), id, recipient_user_id, tenant_id, read_at
                FROM notifications
                WHERE read_at IS NOT NULL AND recipient_user_id IS NOT NULL;
                """);

            // 'Read' is gone from the status enum: that column is the delivery
            // state machine and read-ness was never part of it. An in-app
            // notification is delivered by existing, so Sent is what these were.
            migrationBuilder.Sql(
                """
                UPDATE notifications SET status = 'Sent' WHERE status = 'Read';
                """);

            migrationBuilder.DropColumn(
                name: "read_at",
                table: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // Back down to one read per notification, which is all the old shape
            // could hold. A broadcast read by four hundred people collapses to
            // whichever read was earliest; there is nowhere else for the rest to
            // go, and this direction is for undoing a bad deploy rather than for
            // living in.
            migrationBuilder.Sql(
                """
                UPDATE notifications n
                SET read_at = r.read_at
                FROM (
                    SELECT notification_id, min(read_at) AS read_at
                    FROM notification_reads
                    GROUP BY notification_id) r
                WHERE r.notification_id = n.id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE notifications SET status = 'Read'
                WHERE read_at IS NOT NULL AND channel = 'InApp';
                """);

            migrationBuilder.DropTable(
                name: "notification_reads");
        }
    }
}
