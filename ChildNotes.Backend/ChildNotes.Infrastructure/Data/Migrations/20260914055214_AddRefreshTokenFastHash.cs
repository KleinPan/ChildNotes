using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenFastHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "token_hash_fast",
                table: "refresh_token",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_token_hash_fast",
                table: "refresh_token",
                column: "token_hash_fast");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_token_token_hash_fast",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "token_hash_fast",
                table: "refresh_token");
        }
    }
}
