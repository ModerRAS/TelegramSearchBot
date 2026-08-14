using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Test.Service.Database {
    /// <summary>
    /// LLMApiBinding 相关测试：真实 SQLite 升级路径回填 + 基本 CRUD（InMemory）。
    /// 升级路径必须用真实 SQLite（EF InMemory 不执行迁移）。
    /// </summary>
    public class LlmApiBindingTests {
        private static readonly string LegacyMigration = "20260313124507_AddChannelWithModelIsDeleted";

        private static SqliteConnection OpenSqliteConnection() {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        }

        private static DbContextOptions<DataDbContext> CreateSqliteOptions(SqliteConnection connection) {
            return new DbContextOptionsBuilder<DataDbContext>()
                .UseSqlite(connection)
                .Options;
        }

        private static DbContextOptions<DataDbContext> CreateInMemoryOptions() {
            return new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        /// <summary>
        /// 旧库（legacy 迁移）→ 种子每个 provider 的 channel/model → 迁移到最新 →
        /// 断言 default binding 回填、协议/认证映射、模型 FK 回填、legacy 字段保留。
        /// </summary>
        [Fact]
        public async Task UpgradeFromLegacyDatabase_BackfillsBindingsAndModels() {
            using var connection = OpenSqliteConnection();
            var options = CreateSqliteOptions(connection);

            // 1. 旧库 + 种子 legacy 数据（raw SQL：模拟旧二进制写 legacy schema，
            //    当前模型的 ApiBindingId 列在旧 schema 中不存在）
            using (var ctx = new DataDbContext(options)) {
                ctx.Database.Migrate(LegacyMigration);

                var openaiId = await InsertLegacyChannel(ctx, "openai", "https://api.openai.com/v1", "sk-openai", LLMProvider.OpenAI, 10);
                var ollamaId = await InsertLegacyChannel(ctx, "ollama", "http://localhost:11434", null, LLMProvider.Ollama, 9);
                var geminiId = await InsertLegacyChannel(ctx, "gemini", "https://generativelanguage.googleapis.com", "AI-gemini", LLMProvider.Gemini, 8);
                var minimaxId = await InsertLegacyChannel(ctx, "minimax", "https://api.minimax.chat", "mm-key", LLMProvider.MiniMax, 7);
                var lmstudioId = await InsertLegacyChannel(ctx, "lmstudio", "http://localhost:1234/v1", null, LLMProvider.LMStudio, 6);
                var anthropicId = await InsertLegacyChannel(ctx, "anthropic", "https://api.anthropic.com", "sk-ant", LLMProvider.Anthropic, 5);
                var responsesId = await InsertLegacyChannel(ctx, "responses", "https://api.openai.com/v1", "sk-resp", LLMProvider.ResponsesAPI, 4);
                // 无模型的 channel：仍应获得一个 default binding
                await InsertLegacyChannel(ctx, "empty", "http://localhost:9999/v1", "sk-empty", LLMProvider.OpenAI, 3);

                // 大小写重复对：升级必须成功且两行都在
                await InsertLegacyModel(ctx, openaiId, "gpt-4o", false);
                await InsertLegacyModel(ctx, openaiId, "GPT-4O", false);
                await InsertLegacyModel(ctx, openaiId, "gpt-4o-mini", true);
                await InsertLegacyModel(ctx, ollamaId, "llama3", false);
                await InsertLegacyModel(ctx, geminiId, "gemini-2.5-pro", false);
                await InsertLegacyModel(ctx, minimaxId, "MiniMax-Text-01", false);
                await InsertLegacyModel(ctx, lmstudioId, "qwen2.5-7b", false);
                await InsertLegacyModel(ctx, anthropicId, "claude-sonnet-4-5", false);
                await InsertLegacyModel(ctx, responsesId, "gpt-4.1", false);
            }

            // 2. 升级到最新（含 AddLlmApiBinding 回填）
            using (var ctx = new DataDbContext(options)) {
                ctx.Database.Migrate();
            }

            // 3. 断言
            using (var ctx = new DataDbContext(options)) {
                var channels = await ctx.LLMChannels
                    .Include(c => c.Bindings)
                    .Include(c => c.Models)
                    .OrderBy(c => c.Id)
                    .ToListAsync();

                Assert.Equal(8, channels.Count);

                foreach (var channel in channels) {
                    // 每 channel 恰好一个 default binding
                    var defaults = channel.Bindings.Where(b => b.IsDefault).ToList();
                    Assert.Single(defaults);
                    var binding = defaults[0];

                    // Endpoint 镜像 legacy Gateway
                    Assert.Equal(channel.Gateway, binding.Endpoint);

                    // 协议映射
                    var expectedProtocol = channel.Provider switch {
                        LLMProvider.OpenAI or LLMProvider.MiniMax or LLMProvider.LMStudio => LlmProtocol.OpenAIChat,
                        LLMProvider.ResponsesAPI => LlmProtocol.OpenAIResponses,
                        LLMProvider.Anthropic => LlmProtocol.AnthropicMessages,
                        LLMProvider.Ollama => LlmProtocol.Ollama,
                        LLMProvider.Gemini => LlmProtocol.Gemini,
                        _ => LlmProtocol.OpenAIChat
                    };
                    Assert.Equal(expectedProtocol, binding.Protocol);

                    // 认证映射
                    var expectedAuth = channel.Provider switch {
                        LLMProvider.Anthropic => LlmAuthProfile.AnthropicApiKey,
                        LLMProvider.Ollama => LlmAuthProfile.None,
                        _ => LlmAuthProfile.Bearer
                    };
                    Assert.Equal(expectedAuth, binding.AuthProfile);
                }

                // legacy 字段原样保留
                var openaiChannel = channels.Single(c => c.Name == "openai");
                Assert.Equal("https://api.openai.com/v1", openaiChannel.Gateway);
                Assert.Equal("sk-openai", openaiChannel.ApiKey);
                Assert.Equal(LLMProvider.OpenAI, openaiChannel.Provider);
                Assert.Equal(10, openaiChannel.Priority);

                var ollamaChannel = channels.Single(c => c.Name == "ollama");
                Assert.Equal(LLMProvider.Ollama, ollamaChannel.Provider);
                Assert.Equal(LLMProvider.Anthropic, channels.Single(c => c.Name == "anthropic").Provider);

                // 每个非孤儿模型行都回填了其 channel 的 default binding
                foreach (var channel in channels) {
                    var defaultBinding = channel.Bindings.Single(b => b.IsDefault);
                    foreach (var model in channel.Models) {
                        Assert.NotNull(model.ApiBindingId);
                        Assert.Equal(defaultBinding.Id, model.ApiBindingId);
                        // 回填行默认值
                        Assert.Equal(AuthorizationSource.Manual, model.AuthorizationSource);
                        Assert.False(model.IsPreferred);
                    }
                }

                // 大小写重复对升级后都在，且已回填
                var openaiModels = openaiChannel.Models.Select(m => m.ModelName).OrderBy(n => n).ToList();
                Assert.Contains("gpt-4o", openaiModels);
                Assert.Contains("GPT-4O", openaiModels);
                Assert.Equal(3, openaiModels.Count);

                // IsDeleted 模型也回填
                var deleted = openaiChannel.Models.Single(m => m.IsDeleted);
                Assert.Equal(DefaultBindingId(openaiChannel), deleted.ApiBindingId);

                // 旧二进制模拟：ApiBindingId=NULL 的行可写可查，无 FK 违规
                var legacyWrite = new ChannelWithModel {
                    ModelName = "legacy-write-model",
                    LLMChannelId = openaiChannel.Id,
                    ApiBindingId = null
                };
                ctx.ChannelsWithModel.Add(legacyWrite);
                await ctx.SaveChangesAsync();
                var loaded = await ctx.ChannelsWithModel
                    .AsNoTracking()
                    .SingleAsync(m => m.Id == legacyWrite.Id);
                Assert.Null(loaded.ApiBindingId);
                Assert.Equal(AuthorizationSource.Manual, loaded.AuthorizationSource);
                Assert.False(loaded.IsPreferred);
            }
        }

        private static int DefaultBindingId(LLMChannel channel) {
            return channel.Bindings.Single(b => b.IsDefault).Id;
        }

        /// <summary>
        /// 全新空库直接迁移到最新（含回填 SQL 对空表的幂等性）。
        /// </summary>
        [Fact]
        public void FreshDatabase_MigrateToLatest_SucceedsWithNoChannels() {
            using var connection = OpenSqliteConnection();
            var options = CreateSqliteOptions(connection);
            using var ctx = new DataDbContext(options);
            ctx.Database.Migrate();
            Assert.Empty(ctx.LLMApiBindings);
        }

        private static async Task<int> InsertLegacyChannel(DataDbContext ctx, string name, string gateway, string apiKey,
            LLMProvider provider, int priority) {
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO LLMChannels (Name, Gateway, ApiKey, Provider, Parallel, Priority) VALUES ({0}, {1}, {2}, {3}, 1, {4})",
                name, gateway, apiKey, (int)provider, priority);
            return Convert.ToInt32(await ctx.Database.SqlQueryRaw<long>("SELECT last_insert_rowid() AS Value").SingleAsync());
        }

        private static async Task InsertLegacyModel(DataDbContext ctx, int channelId, string modelName, bool isDeleted) {
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO ChannelsWithModel (ModelName, LLMChannelId, IsDeleted) VALUES ({0}, {1}, {2})",
                modelName, channelId, isDeleted);
        }

        [Fact]
        public async Task LlmApiBindings_BasicCrud() {
            var options = CreateInMemoryOptions();
            using (var ctx = new DataDbContext(options)) {
                var channel = new LLMChannel {
                    Name = "crud",
                    Gateway = "https://example.com/v1",
                    ApiKey = "k",
                    Provider = LLMProvider.OpenAI,
                    Parallel = 1,
                    Priority = 1
                };
                var binding = new LLMApiBinding {
                    LLMChannel = channel,
                    Endpoint = "https://example.com/v1",
                    Protocol = LlmProtocol.OpenAIChat,
                    AuthProfile = LlmAuthProfile.Bearer,
                    IsDefault = true
                };
                ctx.LLMApiBindings.Add(binding);
                await ctx.SaveChangesAsync();
                Assert.True(binding.Id > 0);
            }

            using (var ctx = new DataDbContext(options)) {
                var binding = await ctx.LLMApiBindings
                    .Include(b => b.LLMChannel)
                    .SingleAsync();
                Assert.Equal("crud", binding.LLMChannel.Name);
                Assert.Equal(LlmProtocol.OpenAIChat, binding.Protocol);
                Assert.Equal(LlmAuthProfile.Bearer, binding.AuthProfile);
                Assert.True(binding.IsDefault);
                Assert.Equal("https://example.com/v1", binding.Endpoint);

                // 更新
                binding.AuthProfile = LlmAuthProfile.None;
                binding.IsDefault = false;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new DataDbContext(options)) {
                var binding = await ctx.LLMApiBindings.SingleAsync();
                Assert.Equal(LlmAuthProfile.None, binding.AuthProfile);
                Assert.False(binding.IsDefault);

                // 删除
                ctx.LLMApiBindings.Remove(binding);
                await ctx.SaveChangesAsync();
                Assert.Empty(await ctx.LLMApiBindings.ToListAsync());
            }
        }
    }
}
