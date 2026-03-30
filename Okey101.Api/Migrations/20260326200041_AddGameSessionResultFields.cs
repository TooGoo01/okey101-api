using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Okey101.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSessionResultFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Team1FinalTotal",
                table: "GameSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Team2FinalTotal",
                table: "GameSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WinnerTeamNumber",
                table: "GameSessions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Team1FinalTotal",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "Team2FinalTotal",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "WinnerTeamNumber",
                table: "GameSessions");
        }
    }
}
