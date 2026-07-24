using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiler.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddValuesOpenness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValuesOpenness",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValuesScheme",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValuesOpenness",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ValuesScheme",
                table: "Users");
        }
    }
}
