using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Okey101.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerUsernamePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Players",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Players",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Username",
                table: "Players",
                column: "Username",
                unique: true,
                filter: "[Username] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_Username",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Players");
        }
    }
}
