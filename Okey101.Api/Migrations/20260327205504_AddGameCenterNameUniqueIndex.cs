using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Okey101.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCenterNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GameCenters_Name",
                table: "GameCenters",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameCenters_Name",
                table: "GameCenters");
        }
    }
}
