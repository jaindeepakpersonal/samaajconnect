using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Pathshala.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    marked_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exam_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    recorded_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    exam_date = table.Column<DateOnly>(type: "date", nullable: false),
                    max_score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.id);
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
                name: "pathshalas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pathshalas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_enrolments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_enrolments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "academic_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_academic_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_academic_sessions_pathshalas_pathshala_id",
                        column: x => x.pathshala_id,
                        principalTable: "pathshalas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathshala_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    room_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classes", x => x.id);
                    table.ForeignKey(
                        name: "fk_classes_pathshalas_pathshala_id",
                        column: x => x.pathshala_id,
                        principalTable: "pathshalas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "class_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_class_schedules_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teacher_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_assignments_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_academic_sessions_pathshala_id_label",
                table: "academic_sessions",
                columns: new[] { "pathshala_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_class_id_class_date",
                table: "attendance",
                columns: new[] { "class_id", "class_date" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_enrolment_id_class_date",
                table: "attendance",
                columns: new[] { "enrolment_id", "class_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_schedules_class_id",
                table: "class_schedules",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_classes_pathshala_id",
                table: "classes",
                column: "pathshala_id");

            migrationBuilder.CreateIndex(
                name: "ix_classes_session_id",
                table: "classes",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_enrolment_id",
                table: "exam_results",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_exam_id_enrolment_id",
                table: "exam_results",
                columns: new[] { "exam_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exams_class_id_exam_date",
                table: "exams",
                columns: new[] { "class_id", "exam_date" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pathshalas_tenant_id_status",
                table: "pathshalas",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_child_profile_id",
                table: "student_enrolments",
                column: "child_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_class_id_status",
                table: "student_enrolments",
                columns: new[] { "class_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_pathshala_id_child_profile_id",
                table: "student_enrolments",
                columns: new[] { "pathshala_id", "child_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_pathshala_id_status",
                table: "student_enrolments",
                columns: new[] { "pathshala_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_requested_by_member_id",
                table: "student_enrolments",
                column: "requested_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrolments_student_user_id",
                table: "student_enrolments",
                column: "student_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_assignments_class_id_teacher_member_id",
                table: "teacher_assignments",
                columns: new[] { "class_id", "teacher_member_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "academic_sessions");

            migrationBuilder.DropTable(
                name: "attendance");

            migrationBuilder.DropTable(
                name: "class_schedules");

            migrationBuilder.DropTable(
                name: "exam_results");

            migrationBuilder.DropTable(
                name: "exams");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "student_enrolments");

            migrationBuilder.DropTable(
                name: "teacher_assignments");

            migrationBuilder.DropTable(
                name: "classes");

            migrationBuilder.DropTable(
                name: "pathshalas");
        }
    }
}
