using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSecurityPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_security_policies",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_password_length = table.Column<int>(type: "integer", nullable: true),
                    otp_lifetime_minutes = table.Column<int>(type: "integer", nullable: true),
                    max_failed_login_attempts = table.Column<int>(type: "integer", nullable: true),
                    lockout_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    refresh_token_lifetime_days = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_security_policies", x => x.tenant_id);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "key" },
                values: new object[] { new Guid("b0000000-0000-0000-0000-000000000016"), "Tenant.ManageSecurityPolicy" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000016"), new Guid("a0000000-0000-0000-0000-000000000001") },
                    { new Guid("b0000000-0000-0000-0000-000000000016"), new Guid("a0000000-0000-0000-0000-000000000002") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_security_policies");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000016"), new Guid("a0000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000016"), new Guid("a0000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("b0000000-0000-0000-0000-000000000016"));
        }
    }
}
