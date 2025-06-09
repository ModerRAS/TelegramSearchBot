using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTaskExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTaskExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TaskType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ResultSummary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskExecutions_TaskName_TaskType_ExecutionDate",
                table: "ScheduledTaskExecutions",
                columns: new[] { "TaskName", "TaskType", "ExecutionDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTaskExecutions");
        }
    }
}
