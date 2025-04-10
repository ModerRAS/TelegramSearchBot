using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AppendMessageWithFromAndReplyToessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReplyToMessageId",
                table: "Messages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "Messages");
        }
    }
}
