using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class AddAccountingTables : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "AccountBooks",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_AccountBooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroupAccountSettings",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveAccountBookId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsAccountingEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_GroupAccountSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountRecords",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountBookId = table.Column<long>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_AccountRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountRecords_AccountBooks_AccountBookId",
                        column: x => x.AccountBookId,
                        principalTable: "AccountBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBooks_GroupId_Name",
                table: "AccountBooks",
                columns: new[] { "GroupId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountRecords_AccountBookId_CreatedAt",
                table: "AccountRecords",
                columns: new[] { "AccountBookId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRecords_Tag",
                table: "AccountRecords",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_GroupAccountSettings_GroupId",
                table: "GroupAccountSettings",
                column: "GroupId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "AccountRecords");

            migrationBuilder.DropTable(
                name: "GroupAccountSettings");

            migrationBuilder.DropTable(
                name: "AccountBooks");
        }
    }
}
