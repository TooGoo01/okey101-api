using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Okey101.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCenterLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "GameCenters",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location",
                table: "GameCenters");
        }
    }
}
