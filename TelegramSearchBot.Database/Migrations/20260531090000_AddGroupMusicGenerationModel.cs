using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMusicGenerationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MusicGenerationModelName",
                table: "GroupSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicGenerationModelName",
                table: "GroupSettings");
        }
    }
}
