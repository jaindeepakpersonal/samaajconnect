using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activation_code_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "activation_code_failed_attempts",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activation_code_hash",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activation_code_issued_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "activation_code_issued_by",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "converted_from_child_profile_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_converted_from_child_profile_id",
                table: "users",
                column: "converted_from_child_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_status",
                table: "users",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_converted_from_child_profile_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_code_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_code_failed_attempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_code_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_code_issued_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_code_issued_by",
                table: "users");

            migrationBuilder.DropColumn(
                name: "converted_from_child_profile_id",
                table: "users");
        }
    }
}
