using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mobile_or_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    auth_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_contact_verified = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_out_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_scope = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "key" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000001"), "Tenant.Manage" },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), "AdminUsers.Manage" },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), "Members.Read" },
                    { new Guid("b0000000-0000-0000-0000-000000000004"), "Members.Write" },
                    { new Guid("b0000000-0000-0000-0000-000000000005"), "Family.Write" },
                    { new Guid("b0000000-0000-0000-0000-000000000006"), "Family.ApproveConversion" },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), "Timeline.Post" },
                    { new Guid("b0000000-0000-0000-0000-000000000008"), "Timeline.Moderate" },
                    { new Guid("b0000000-0000-0000-0000-000000000009"), "VolunteerGroups.Manage" },
                    { new Guid("b0000000-0000-0000-0000-00000000000a"), "Events.Publish" },
                    { new Guid("b0000000-0000-0000-0000-00000000000b"), "SocialIssues.Approve" },
                    { new Guid("b0000000-0000-0000-0000-00000000000c"), "CelebrityVoting.Configure" },
                    { new Guid("b0000000-0000-0000-0000-00000000000d"), "Pathshala.Manage" },
                    { new Guid("b0000000-0000-0000-0000-00000000000e"), "Pathshala.Attendance.Write" },
                    { new Guid("b0000000-0000-0000-0000-00000000000f"), "Pathshala.Exams.Write" },
                    { new Guid("b0000000-0000-0000-0000-000000000010"), "Boli.Manage" },
                    { new Guid("b0000000-0000-0000-0000-000000000011"), "Boli.PublishResults" },
                    { new Guid("b0000000-0000-0000-0000-000000000012"), "Audit.Read" }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), "SuperAdmin" },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), "SamaajAdmin" },
                    { new Guid("a0000000-0000-0000-0000-000000000003"), "Member" },
                    { new Guid("a0000000-0000-0000-0000-000000000004"), "FamilyHead" },
                    { new Guid("a0000000-0000-0000-0000-000000000005"), "VolunteerGroupPresident" },
                    { new Guid("a0000000-0000-0000-0000-000000000006"), "PathshalaTeacher" },
                    { new Guid("a0000000-0000-0000-0000-000000000007"), "PathshalaStudent" },
                    { new Guid("a0000000-0000-0000-0000-000000000008"), "ContentModerator" },
                    { new Guid("a0000000-0000-0000-0000-000000000009"), "BoliManager" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000001"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000004"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000005"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000006"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000008"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000009"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000a"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000b"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000c"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000d"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000e"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-00000000000f"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000010"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000011"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000012"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000004"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000005"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000006"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000008"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000009"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-00000000000a"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-00000000000b"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-00000000000c"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000010"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000011"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000012"), new Guid("a0000000-0000-0000-0000-000000000002") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000003") },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), new Guid("a0000000-0000-0000-0000-000000000003") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000004") },
                    { new Guid("b0000000-0000-0000-0000-000000000005"), new Guid("a0000000-0000-0000-0000-000000000004") },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), new Guid("a0000000-0000-0000-0000-000000000004") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000005") },
                    { new Guid("b0000000-0000-0000-0000-000000000007"), new Guid("a0000000-0000-0000-0000-000000000005") },
                    { new Guid("b0000000-0000-0000-0000-000000000009"), new Guid("a0000000-0000-0000-0000-000000000005") },
                    { new Guid("b0000000-0000-0000-0000-00000000000a"), new Guid("a0000000-0000-0000-0000-000000000005") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000006") },
                    { new Guid("b0000000-0000-0000-0000-00000000000e"), new Guid("a0000000-0000-0000-0000-000000000006") },
                    { new Guid("b0000000-0000-0000-0000-00000000000f"), new Guid("a0000000-0000-0000-0000-000000000006") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000007") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000008") },
                    { new Guid("b0000000-0000-0000-0000-000000000008"), new Guid("a0000000-0000-0000-0000-000000000008") },
                    { new Guid("b0000000-0000-0000-0000-00000000000b"), new Guid("a0000000-0000-0000-0000-000000000008") },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-000000000009") },
                    { new Guid("b0000000-0000-0000-0000-000000000010"), new Guid("a0000000-0000-0000-0000-000000000009") },
                    { new Guid("b0000000-0000-0000-0000-000000000011"), new Guid("a0000000-0000-0000-0000-000000000009") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_key",
                table: "permissions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_id_tenant_scope",
                table: "user_roles",
                columns: new[] { "user_id", "role_id", "tenant_scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_mobile_or_email",
                table: "users",
                column: "mobile_or_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id",
                table: "users",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
