using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Pathshala.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeachersDonationsAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "teacher_member_id",
                table: "teacher_assignments",
                newName: "teacher_id");

            migrationBuilder.RenameIndex(
                name: "ix_teacher_assignments_class_id_teacher_member_id",
                table: "teacher_assignments",
                newName: "ix_teacher_assignments_class_id_teacher_id");

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "student_enrolments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                table: "student_enrolments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pathshala_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pathshala_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pathshala_donations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<int>(type: "integer", nullable: false),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    donor_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    donated_on = table.Column<DateOnly>(type: "date", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pathshala_donations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pathshala_teachers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    qualification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pathshala_teachers", x => x.id);
                    table.ForeignKey(
                        name: "fk_pathshala_teachers_pathshalas_pathshala_id",
                        column: x => x.pathshala_id,
                        principalTable: "pathshalas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_assignments_teacher_id",
                table: "teacher_assignments",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_pathshala_announcements_pathshala_id_sent_at",
                table: "pathshala_announcements",
                columns: new[] { "pathshala_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pathshala_donations_pathshala_id_donated_on",
                table: "pathshala_donations",
                columns: new[] { "pathshala_id", "donated_on" });

            migrationBuilder.CreateIndex(
                name: "ix_pathshala_donations_pathshala_id_receipt_number",
                table: "pathshala_donations",
                columns: new[] { "pathshala_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pathshala_teachers_pathshala_id_user_id",
                table: "pathshala_teachers",
                columns: new[] { "pathshala_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pathshala_teachers_user_id",
                table: "pathshala_teachers",
                column: "user_id");

            // Until now a class's teacher was a member id - renamed to teacher_id
            // above but still holding member ids. Give each distinct teacher of
            // each Pathshala a record, linked to the same account so they keep
            // marking their registers, and point the assignments at it. Names
            // were never held here, so the record says to add them.
            migrationBuilder.Sql("""
                INSERT INTO pathshala_teachers (id, pathshala_id, full_name, user_id, added_at)
                SELECT gen_random_uuid(), c.pathshala_id, 'Teacher (add their details)', a.teacher_id, min(a.assigned_at)
                FROM teacher_assignments a
                JOIN classes c ON c.id = a.class_id
                GROUP BY c.pathshala_id, a.teacher_id;

                UPDATE teacher_assignments a
                SET teacher_id = t.id
                FROM classes c, pathshala_teachers t
                WHERE c.id = a.class_id AND t.pathshala_id = c.pathshala_id AND t.user_id = a.teacher_id;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_assignments_teacher_teacher_id",
                table: "teacher_assignments",
                column: "teacher_id",
                principalTable: "pathshala_teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_teacher_assignments_teacher_teacher_id",
                table: "teacher_assignments");

            migrationBuilder.DropTable(
                name: "pathshala_announcements");

            migrationBuilder.DropTable(
                name: "pathshala_donations");

            migrationBuilder.DropTable(
                name: "pathshala_teachers");

            migrationBuilder.DropIndex(
                name: "ix_teacher_assignments_teacher_id",
                table: "teacher_assignments");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "student_enrolments");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                table: "student_enrolments");

            migrationBuilder.RenameColumn(
                name: "teacher_id",
                table: "teacher_assignments",
                newName: "teacher_member_id");

            migrationBuilder.RenameIndex(
                name: "ix_teacher_assignments_class_id_teacher_id",
                table: "teacher_assignments",
                newName: "ix_teacher_assignments_class_id_teacher_member_id");
        }
    }
}
