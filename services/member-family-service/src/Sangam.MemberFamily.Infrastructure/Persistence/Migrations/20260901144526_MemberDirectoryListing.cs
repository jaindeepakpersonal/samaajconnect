using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sangam.MemberFamily.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberDirectoryListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Listed by default, so nobody vanishes from their Samaaj's
            // directory because a column arrived.
            migrationBuilder.AddColumn<bool>(
                name: "is_listed_in_directory",
                table: "member_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Except the profiles that were already erased. Those rows are kept
            // only so family links do not dangle; until now they stayed in the
            // directory as a row reading "Erased member", because there was no
            // way to express "keep the row, drop the listing". There is now,
            // and it applies to the ones that came before it.
            //
            // Matched on the tombstone name because that is the marker
            // MemberProfile.Erase writes and the only one these rows carry - an
            // erased profile is otherwise indistinguishable from a member who
            // has filled nothing in.
            migrationBuilder.Sql(
                """
                UPDATE member_profiles
                SET is_listed_in_directory = false
                WHERE full_name = 'Erased member';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_listed_in_directory",
                table: "member_profiles");
        }
    }
}
