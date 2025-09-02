using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class AddConversationSegments : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "ConversationSegments",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentSummary = table.Column<string>(type: "TEXT", nullable: true),
                    TopicKeywords = table.Column<string>(type: "TEXT", nullable: true),
                    FullContent = table.Column<string>(type: "TEXT", nullable: true),
                    VectorId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsVectorized = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_ConversationSegments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSegmentMessages",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationSegmentId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageDataId = table.Column<long>(type: "INTEGER", nullable: false),
                    SequenceOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_ConversationSegmentMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationSegmentMessages_ConversationSegments_ConversationSegmentId",
                        column: x => x.ConversationSegmentId,
                        principalTable: "ConversationSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationSegmentMessages_Messages_MessageDataId",
                        column: x => x.MessageDataId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSegmentMessages_ConversationSegmentId",
                table: "ConversationSegmentMessages",
                column: "ConversationSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSegmentMessages_MessageDataId",
                table: "ConversationSegmentMessages",
                column: "MessageDataId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSegments_GroupId_StartTime_EndTime",
                table: "ConversationSegments",
                columns: new[] { "GroupId", "StartTime", "EndTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "ConversationSegmentMessages");

            migrationBuilder.DropTable(
                name: "ConversationSegments");
        }
    }
}
