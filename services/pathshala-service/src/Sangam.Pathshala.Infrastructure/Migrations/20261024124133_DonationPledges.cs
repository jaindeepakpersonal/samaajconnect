using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Pathshala.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DonationPledges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "receipt_number",
                table: "pathshala_donations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "pathshala_donations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_on",
                table: "pathshala_donations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "pathshala_donations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Paid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "pathshala_donations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                table: "pathshala_donations",
                type: "uuid",
                nullable: true);

            // Every donation recorded before pledges existed was money received,
            // with a receipt: Paid, by its own mode, on the day it was given.
            migrationBuilder.Sql("UPDATE pathshala_donations SET paid_on = donated_on WHERE status = 'Paid';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_on",
                table: "pathshala_donations");

            migrationBuilder.DropColumn(
                name: "status",
                table: "pathshala_donations");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "pathshala_donations");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "pathshala_donations");

            migrationBuilder.AlterColumn<int>(
                name: "receipt_number",
                table: "pathshala_donations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "pathshala_donations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
