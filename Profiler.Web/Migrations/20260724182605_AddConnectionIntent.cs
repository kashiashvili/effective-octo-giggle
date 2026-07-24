using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiler.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionIntent",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionIntent",
                table: "Users");
        }
    }
}
