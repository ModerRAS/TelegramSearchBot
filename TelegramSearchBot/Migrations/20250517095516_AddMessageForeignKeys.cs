using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Messages_GroupId_MessageId",
                table: "Messages",
                columns: new[] { "GroupId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_FromUserId",
                table: "Messages",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_GroupId_ReplyToMessageId",
                table: "Messages",
                columns: new[] { "GroupId", "ReplyToMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReplyToUserId",
                table: "Messages",
                column: "ReplyToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_GroupData_GroupId",
                table: "Messages",
                column: "GroupId",
                principalTable: "GroupData",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Messages_GroupId_ReplyToMessageId",
                table: "Messages",
                columns: new[] { "GroupId", "ReplyToMessageId" },
                principalTable: "Messages",
                principalColumns: new[] { "GroupId", "MessageId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_UserData_FromUserId",
                table: "Messages",
                column: "FromUserId",
                principalTable: "UserData",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_UserData_ReplyToUserId",
                table: "Messages",
                column: "ReplyToUserId",
                principalTable: "UserData",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_GroupData_GroupId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Messages_GroupId_ReplyToMessageId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_UserData_FromUserId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_UserData_ReplyToUserId",
                table: "Messages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Messages_GroupId_MessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_FromUserId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_GroupId_ReplyToMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ReplyToUserId",
                table: "Messages");
        }
    }
}
