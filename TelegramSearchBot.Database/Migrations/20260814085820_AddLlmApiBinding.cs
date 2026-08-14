using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramSearchBot.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmApiBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApiBindingId",
                table: "ChannelsWithModel",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizationSource",
                table: "ChannelsWithModel",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPreferred",
                table: "ChannelsWithModel",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LLMApiBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LLMChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthProfile = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMApiBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LLMApiBindings_LLMChannels_LLMChannelId",
                        column: x => x.LLMChannelId,
                        principalTable: "LLMChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelsWithModel_ApiBindingId",
                table: "ChannelsWithModel",
                column: "ApiBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_LLMApiBindings_LLMChannelId",
                table: "LLMApiBindings",
                column: "LLMChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelsWithModel_LLMApiBindings_ApiBindingId",
                table: "ChannelsWithModel",
                column: "ApiBindingId",
                principalTable: "LLMApiBindings",
                principalColumn: "Id");

            // ---- 数据回填（每个旧 channel 恰好一个 default binding；模型回填绑定）----
            // 协议映射：OpenAI/MiniMax/LMStudio→OpenAIChat(0)；ResponsesAPI→OpenAIResponses(1)；
            // Anthropic→AnthropicMessages(2)；Ollama→Ollama(3)；Gemini→Gemini(4)；其余→OpenAIChat(0)。
            // 认证映射：Anthropic→AnthropicApiKey(1)；Ollama→None(2)（OllamaService 源码证明 keyless）；其余→Bearer(0)。
            migrationBuilder.Sql(@"
INSERT INTO LLMApiBindings (LLMChannelId, Endpoint, Protocol, AuthProfile, IsDefault)
SELECT Id, Gateway,
       CASE Provider
           WHEN 1 THEN 0 -- OpenAI -> OpenAIChat
           WHEN 2 THEN 3 -- Ollama -> Ollama
           WHEN 3 THEN 4 -- Gemini -> Gemini
           WHEN 4 THEN 0 -- MiniMax -> OpenAIChat
           WHEN 5 THEN 0 -- LMStudio -> OpenAIChat
           WHEN 6 THEN 2 -- Anthropic -> AnthropicMessages
           WHEN 7 THEN 1 -- ResponsesAPI -> OpenAIResponses
           ELSE 0
       END,
       CASE Provider
           WHEN 6 THEN 1 -- Anthropic -> AnthropicApiKey
           WHEN 2 THEN 2 -- Ollama -> None（keyless）
           ELSE 0       -- 其余 -> Bearer
       END,
       1
FROM LLMChannels;");

            // 回填旧模型行：指向其 channel 的 default binding；孤儿行保持 NULL（legacy fallback）
            migrationBuilder.Sql(@"
UPDATE ChannelsWithModel
SET ApiBindingId = (SELECT b.Id FROM LLMApiBindings b
                    WHERE b.LLMChannelId = ChannelsWithModel.LLMChannelId
                      AND b.IsDefault = 1 LIMIT 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelsWithModel_LLMApiBindings_ApiBindingId",
                table: "ChannelsWithModel");

            migrationBuilder.DropTable(
                name: "LLMApiBindings");

            migrationBuilder.DropIndex(
                name: "IX_ChannelsWithModel_ApiBindingId",
                table: "ChannelsWithModel");

            migrationBuilder.DropColumn(
                name: "ApiBindingId",
                table: "ChannelsWithModel");

            migrationBuilder.DropColumn(
                name: "AuthorizationSource",
                table: "ChannelsWithModel");

            migrationBuilder.DropColumn(
                name: "IsPreferred",
                table: "ChannelsWithModel");
        }
    }
}
