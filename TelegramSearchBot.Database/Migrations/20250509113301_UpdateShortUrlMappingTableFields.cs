using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class UpdateShortUrlMappingTableFields : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropIndex(
                name: "IX_ShortUrlMappings_ShortCode",
                table: "ShortUrlMappings");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "ShortUrlMappings");

            migrationBuilder.RenameColumn(
                name: "LongUrl",
                table: "ShortUrlMappings",
                newName: "OriginalUrl");

            migrationBuilder.AddColumn<string>(
                name: "ExpandedUrl",
                table: "ShortUrlMappings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ShortUrlMappings_OriginalUrl",
                table: "ShortUrlMappings",
                column: "OriginalUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropIndex(
                name: "IX_ShortUrlMappings_OriginalUrl",
                table: "ShortUrlMappings");

            migrationBuilder.DropColumn(
                name: "ExpandedUrl",
                table: "ShortUrlMappings");

            migrationBuilder.RenameColumn(
                name: "OriginalUrl",
                table: "ShortUrlMappings",
                newName: "LongUrl");

            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "ShortUrlMappings",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ShortUrlMappings_ShortCode",
                table: "ShortUrlMappings",
                column: "ShortCode",
                unique: true);
        }
    }
}
