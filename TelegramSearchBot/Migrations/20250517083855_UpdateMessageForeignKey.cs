using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMessageForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MessageExtensions_MessageDataId",
                table: "MessageExtensions",
                column: "MessageDataId");

            migrationBuilder.AddForeignKey(
                name: "FK_MessageExtensions_Messages_MessageDataId",
                table: "MessageExtensions",
                column: "MessageDataId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MessageExtensions_Messages_MessageDataId",
                table: "MessageExtensions");

            migrationBuilder.DropIndex(
                name: "IX_MessageExtensions_MessageDataId",
                table: "MessageExtensions");
        }
    }
}
