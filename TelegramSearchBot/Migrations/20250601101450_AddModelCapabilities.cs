using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddModelCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelCapabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelWithModelId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityName = table.Column<string>(type: "TEXT", nullable: false),
                    CapabilityValue = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCapabilities_ChannelsWithModel_ChannelWithModelId",
                        column: x => x.ChannelWithModelId,
                        principalTable: "ChannelsWithModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCapabilities_ChannelWithModelId",
                table: "ModelCapabilities",
                column: "ChannelWithModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelCapabilities");
        }
    }
}
