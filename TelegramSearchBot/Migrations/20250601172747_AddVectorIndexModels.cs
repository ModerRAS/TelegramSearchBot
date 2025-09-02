using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations {
    /// <inheritdoc />
    public partial class AddVectorIndexModels : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "FaissIndexFiles",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    VectorCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_FaissIndexFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VectorIndexes",
                columns: table => new {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<long>(type: "INTEGER", nullable: false),
                    VectorType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    FaissIndex = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_VectorIndexes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaissIndexFiles_GroupId_IndexType",
                table: "FaissIndexFiles",
                columns: new[] { "GroupId", "IndexType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VectorIndexes_GroupId_FaissIndex",
                table: "VectorIndexes",
                columns: new[] { "GroupId", "FaissIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_VectorIndexes_GroupId_VectorType_EntityId",
                table: "VectorIndexes",
                columns: new[] { "GroupId", "VectorType", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "FaissIndexFiles");

            migrationBuilder.DropTable(
                name: "VectorIndexes");
        }
    }
}
