using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class AddTelegramFileCache : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "TelegramFileCacheEntries",
                columns: table => new {
                    CacheKey = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table => {
                    table.PrimaryKey("PK_TelegramFileCacheEntries", x => x.CacheKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramFileCacheEntries_CacheKey",
                table: "TelegramFileCacheEntries",
                column: "CacheKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "TelegramFileCacheEntries");
        }
    }
}
