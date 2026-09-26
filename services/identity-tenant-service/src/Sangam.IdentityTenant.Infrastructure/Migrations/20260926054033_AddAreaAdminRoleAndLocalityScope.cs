using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sangam.IdentityTenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaAdminRoleAndLocalityScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "locality_id",
                table: "user_roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "name" },
                values: new object[] { new Guid("a0000000-0000-0000-0000-00000000000a"), "AreaAdmin" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-00000000000a") },
                    { new Guid("b0000000-0000-0000-0000-000000000004"), new Guid("a0000000-0000-0000-0000-00000000000a") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000003"), new Guid("a0000000-0000-0000-0000-00000000000a") });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("b0000000-0000-0000-0000-000000000004"), new Guid("a0000000-0000-0000-0000-00000000000a") });

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000a"));

            migrationBuilder.DropColumn(
                name: "locality_id",
                table: "user_roles");
        }
    }
}
