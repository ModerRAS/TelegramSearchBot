using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class AddLastHeartbeat : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeat",
                table: "ScheduledTaskExecutions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "LastHeartbeat",
                table: "ScheduledTaskExecutions");
        }
    }
}
