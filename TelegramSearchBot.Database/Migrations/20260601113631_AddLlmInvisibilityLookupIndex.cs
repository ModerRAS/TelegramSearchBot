using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmInvisibilityLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UsersWithGroup_GroupId_IsLlmInvisible",
                table: "UsersWithGroup",
                columns: new[] { "GroupId", "IsLlmInvisible" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsersWithGroup_GroupId_IsLlmInvisible",
                table: "UsersWithGroup");
        }
    }
}
