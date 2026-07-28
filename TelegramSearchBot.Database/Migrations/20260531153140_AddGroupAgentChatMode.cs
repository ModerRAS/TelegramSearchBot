using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupAgentChatMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentChatBatchWindowSeconds",
                table: "GroupSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentChatMode",
                table: "GroupSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgentChatEnabled",
                table: "GroupSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentChatBatchWindowSeconds",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "AgentChatMode",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "IsAgentChatEnabled",
                table: "GroupSettings");
        }
    }
}
