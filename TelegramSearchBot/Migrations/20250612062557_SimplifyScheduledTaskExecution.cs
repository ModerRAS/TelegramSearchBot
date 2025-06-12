using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyScheduledTaskExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledTaskExecutions_TaskName_TaskType_ExecutionDate",
                table: "ScheduledTaskExecutions");

            migrationBuilder.DropColumn(
                name: "ExecutionDate",
                table: "ScheduledTaskExecutions");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "ScheduledTaskExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskExecutions_TaskName",
                table: "ScheduledTaskExecutions",
                column: "TaskName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledTaskExecutions_TaskName",
                table: "ScheduledTaskExecutions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionDate",
                table: "ScheduledTaskExecutions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TaskType",
                table: "ScheduledTaskExecutions",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTaskExecutions_TaskName_TaskType_ExecutionDate",
                table: "ScheduledTaskExecutions",
                columns: new[] { "TaskName", "TaskType", "ExecutionDate" },
                unique: true);
        }
    }
}
