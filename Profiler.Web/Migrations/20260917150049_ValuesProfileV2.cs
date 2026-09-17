using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiler.Web.Migrations
{
    /// <inheritdoc />
    public partial class ValuesProfileV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // v1 buckets ("openness-v1", one axis, no centring) are not comparable with the v2 profile
            // and the column that held them goes below; clear the scheme tag so nobody appears to have
            // a profile they must in fact answer afresh. No live users exist at this point.
            migrationBuilder.Sql("UPDATE Users SET ValuesScheme = NULL WHERE ValuesScheme = 'openness-v1';");
            migrationBuilder.DropColumn(
                name: "ValuesOpenness",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "ValuesProfileJson",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValuesProfileJson",
                table: "Users");

            migrationBuilder.AddColumn<int>(
                name: "ValuesOpenness",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }
    }
}
