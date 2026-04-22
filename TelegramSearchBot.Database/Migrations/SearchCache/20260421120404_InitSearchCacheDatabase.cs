using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Database.Migrations.SearchCache
{
    /// <inheritdoc />
    public partial class InitSearchCacheDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchPageCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UUID = table.Column<string>(type: "TEXT", nullable: false),
                    SearchOptionJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchPageCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchPageCaches_UUID",
                table: "SearchPageCaches",
                column: "UUID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchPageCaches");
        }
    }
}
