using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorApp4.Migrations
{
    /// <inheritdoc />
    public partial class Updetaersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Versions_Versions_VersionDbId",
                table: "Versions");

            migrationBuilder.DropIndex(
                name: "IX_Versions_VersionDbId",
                table: "Versions");

            migrationBuilder.DropColumn(
                name: "VersionDbId",
                table: "Versions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VersionDbId",
                table: "Versions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Versions_VersionDbId",
                table: "Versions",
                column: "VersionDbId");

            migrationBuilder.AddForeignKey(
                name: "FK_Versions_Versions_VersionDbId",
                table: "Versions",
                column: "VersionDbId",
                principalTable: "Versions",
                principalColumn: "Id");
        }
    }
}
