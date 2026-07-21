using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiler.Web.Migrations
{
    /// <inheritdoc />
    public partial class CascadeUserBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Blocks were previously left behind when an account was deleted. SQLite's table rebuild
            // copies rows without validating them, so those orphans would survive the new constraint
            // and keep referring to people who asked to be erased. Clear them first.
            migrationBuilder.Sql(
                "DELETE FROM UserBlocks WHERE BlockerId NOT IN (SELECT Id FROM Users) " +
                "OR BlockedId NOT IN (SELECT Id FROM Users);");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedId",
                table: "UserBlocks",
                column: "BlockedId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserBlocks_Users_BlockedId",
                table: "UserBlocks",
                column: "BlockedId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserBlocks_Users_BlockerId",
                table: "UserBlocks",
                column: "BlockerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserBlocks_Users_BlockedId",
                table: "UserBlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_UserBlocks_Users_BlockerId",
                table: "UserBlocks");

            migrationBuilder.DropIndex(
                name: "IX_UserBlocks_BlockedId",
                table: "UserBlocks");
        }
    }
}
