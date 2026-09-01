using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sangam.IdentityTenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditableRoleMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_permission_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permission_overrides", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "key" },
                values: new object[] { new Guid("b0000000-0000-0000-0000-000000000014"), "Roles.Manage" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000014"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000014"), new Guid("a0000000-0000-0000-0000-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_overrides_tenant_id_role_id_permission_id",
                table: "role_permission_overrides",
                columns: new[] { "tenant_id", "role_id", "permission_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permission_overrides");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000014"), new Guid("a0000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000014"), new Guid("a0000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("b0000000-0000-0000-0000-000000000014"));
        }
    }
}
