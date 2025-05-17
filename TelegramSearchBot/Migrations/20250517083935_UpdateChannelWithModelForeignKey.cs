using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateChannelWithModelForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ChannelsWithModel_LLMChannelId",
                table: "ChannelsWithModel",
                column: "LLMChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelsWithModel_LLMChannels_LLMChannelId",
                table: "ChannelsWithModel",
                column: "LLMChannelId",
                principalTable: "LLMChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelsWithModel_LLMChannels_LLMChannelId",
                table: "ChannelsWithModel");

            migrationBuilder.DropIndex(
                name: "IX_ChannelsWithModel_LLMChannelId",
                table: "ChannelsWithModel");
        }
    }
}
