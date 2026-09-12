using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.Pathshala.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestructurePathshalaAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "address",
                table: "pathshalas",
                newName: "address_line");

            migrationBuilder.AddColumn<Guid>(
                name: "address_city_id",
                table: "pathshalas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "address_country_id",
                table: "pathshalas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "address_locality_id",
                table: "pathshalas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "address_state_id",
                table: "pathshalas",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_city_id",
                table: "pathshalas");

            migrationBuilder.DropColumn(
                name: "address_country_id",
                table: "pathshalas");

            migrationBuilder.DropColumn(
                name: "address_locality_id",
                table: "pathshalas");

            migrationBuilder.DropColumn(
                name: "address_state_id",
                table: "pathshalas");

            migrationBuilder.RenameColumn(
                name: "address_line",
                table: "pathshalas",
                newName: "address");
        }
    }
}
