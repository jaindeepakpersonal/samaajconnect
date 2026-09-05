using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "login_otp_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "login_otp_hash",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "login_otp_issued_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "login_otp_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "login_otp_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "login_otp_issued_at",
                table: "users");
        }
    }
}
