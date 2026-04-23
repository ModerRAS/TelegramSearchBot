using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoReminderTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TodoLists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedBy = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TodoItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    TodoListId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedBy = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReminderMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemindAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderSentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TodoItems_TodoLists_TodoListId",
                        column: x => x.TodoListId,
                        principalTable: "TodoLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_ChatId_Status_RemindAtUtc_ReminderSentAtUtc",
                table: "TodoItems",
                columns: new[] { "ChatId", "Status", "RemindAtUtc", "ReminderSentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_TodoListId_Status",
                table: "TodoItems",
                columns: new[] { "TodoListId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TodoLists_ChatId_Name",
                table: "TodoLists",
                columns: new[] { "ChatId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TodoItems");

            migrationBuilder.DropTable(
                name: "TodoLists");
        }
    }
}
