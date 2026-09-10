using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Storage;
using Xunit;

namespace TelegramSearchBot.Test.Manage {
    public class EditLLMConfHelperTest {
        private readonly DataDbContext _context;
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<OpenAIService> _openAIServiceMock;
        private readonly Mock<MessageExtensionService> _messageExtensionServiceMock;
        private readonly Mock<OllamaService> _ollamaServiceMock;
        private readonly Mock<GeminiService> _geminiServiceMock;
        private readonly Mock<IModelCapabilityService> _modelCapabilityServiceMock;
        private readonly Mock<ILogger<EditLLMConfHelper>> _loggerMock;
        private readonly EditLLMConfHelper _helper;

        public EditLLMConfHelperTest() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new DataDbContext(options);

            // Setup common mocks
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var openAiLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpenAIService>();
            var ollamaLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OllamaService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            _messageExtensionServiceMock = new Mock<MessageExtensionService>(_context);

            // Setup ModelCapabilityService mock
            _modelCapabilityServiceMock = new Mock<IModelCapabilityService>();
            _modelCapabilityServiceMock
                .Setup(m => m.UpdateChannelModelCapabilities(It.IsAny<int>()))
                .ReturnsAsync(true);

            // Setup mock LLM services
            _ollamaServiceMock = new Mock<OllamaService>(
                _context,
                ollamaLogger,
                serviceProviderMock.Object,
                httpClientFactoryMock.Object);
            _ollamaServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "ollama-model1", "ollama-model2" });

            _geminiServiceMock = new Mock<GeminiService>();
            _geminiServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "gemini-model1", "gemini-model2" });

            var openAiLoggerMock = new Mock<ILogger<OpenAIService>>();
            _openAIServiceMock = new Mock<OpenAIService>(
                _context,
                openAiLoggerMock.Object,
                _messageExtensionServiceMock.Object,
                httpClientFactoryMock.Object);
            _openAIServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "openai-model1", "openai-model2" });

            var geminiLoggerMock = new Mock<ILogger<GeminiService>>();
            _geminiServiceMock = new Mock<GeminiService>(_context, geminiLoggerMock.Object, httpClientFactoryMock.Object);

            // 新增 ILLMFactory mock
            var llmFactoryMock = new Mock<ILLMFactory>();
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.OpenAI)).Returns(_openAIServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.MiniMax)).Returns(_openAIServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Ollama)).Returns(_ollamaServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Gemini)).Returns(_geminiServiceMock.Object);

            // 创建Logger mock
            _loggerMock = new Mock<ILogger<EditLLMConfHelper>>();

            _helper = new EditLLMConfHelper(
                _context,
                llmFactoryMock.Object,
                _modelCapabilityServiceMock.Object,
                _loggerMock.Object);

            _messageExtensionServiceMock.Setup(m => m.AddOrUpdateAsync(It.IsAny<MessageExtension>()))
                .Returns(Task.CompletedTask);
            _geminiServiceMock.Setup(g => g.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "gemini-model1", "gemini-model2" });
        }

        [Fact]
        public async Task RefreshAllChannel_ShouldMarkDeletedModels_WhenModelDisappears() {
            // Arrange: channel with default binding; a Discovered row whose model disappears
            // from the catalog gets soft-deleted; a Manual row never does (blueprint §四.5)
            var channel = new LLMChannel { Id = 10, Name = "OpenAI", Provider = LLMProvider.OpenAI, Gateway = "http://gw" };
            await _context.LLMChannels.AddAsync(channel);
            var binding = new LLMApiBinding { LLMChannelId = 10, Endpoint = "http://gw", Protocol = LlmProtocol.OpenAIChat, AuthProfile = LlmAuthProfile.Bearer, IsDefault = true };
            await _context.LLMApiBindings.AddAsync(binding);
            await _context.SaveChangesAsync();
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 10, ModelName = "openai-model1", IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id },
                new ChannelWithModel { LLMChannelId = 10, ModelName = "old-model", IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id },
                new ChannelWithModel { LLMChannelId = 10, ModelName = "manual-model", IsDeleted = false, AuthorizationSource = AuthorizationSource.Manual }
            });
            await _context.SaveChangesAsync();

            // Mock returns ["openai-model1", "openai-model2"] (old-model + manual-model missing from catalog)

            // Act
            await _helper.RefreshAllChannel();

            // Assert: Discovered row missing from catalog is soft-deleted
            var oldModel = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 10 && m.ModelName == "old-model");
            Assert.NotNull(oldModel);
            Assert.True(oldModel.IsDeleted);

            // Manual row is NEVER soft-deleted by refresh, even when missing from catalog
            var manualModel = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 10 && m.ModelName == "manual-model");
            Assert.NotNull(manualModel);
            Assert.False(manualModel.IsDeleted);

            // openai-model1 should still exist and not be deleted
            var model1 = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 10 && m.ModelName == "openai-model1");
            Assert.NotNull(model1);
            Assert.False(model1.IsDeleted);
        }

        [Fact]
        public async Task RefreshAllChannel_ShouldRestoreModels_WhenModelReappears() {
            // Arrange: channel with default binding; a previously-deleted Discovered row is
            // restored when its model reappears in the catalog; a deleted Manual row is never resurrected
            var channel = new LLMChannel { Id = 11, Name = "OpenAI", Provider = LLMProvider.OpenAI, Gateway = "http://gw" };
            await _context.LLMChannels.AddAsync(channel);
            var binding = new LLMApiBinding { LLMChannelId = 11, Endpoint = "http://gw", Protocol = LlmProtocol.OpenAIChat, AuthProfile = LlmAuthProfile.Bearer, IsDefault = true };
            await _context.LLMApiBindings.AddAsync(binding);
            await _context.SaveChangesAsync();
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 11, ModelName = "openai-model1", IsDeleted = true, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id },
                new ChannelWithModel { LLMChannelId = 11, ModelName = "manual-gone", IsDeleted = true, AuthorizationSource = AuthorizationSource.Manual }
            });
            await _context.SaveChangesAsync();

            // Mock returns ["openai-model1", "openai-model2"] – openai-model1 is back

            // Act
            var count = await _helper.RefreshAllChannel();

            // Assert: Discovered row restored
            var model1 = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 11 && m.ModelName == "openai-model1");
            Assert.NotNull(model1);
            Assert.False(model1.IsDeleted);

            // Manual row is NEVER resurrected by refresh
            var manualGone = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 11 && m.ModelName == "manual-gone");
            Assert.NotNull(manualGone);
            Assert.True(manualGone.IsDeleted);

            // openai-model2 should be newly added as Discovered
            var model2 = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == 11 && m.ModelName == "openai-model2");
            Assert.NotNull(model2);
            Assert.False(model2.IsDeleted);
            Assert.Equal(AuthorizationSource.Discovered, model2.AuthorizationSource);
            Assert.Equal(binding.Id, model2.ApiBindingId);

            // count reflects restored + added
            Assert.Equal(2, count);  // 1 restored + 1 added
        }

        [Fact]
        public async Task RefreshAllChannel_MiniMax_ShouldPreserveMissingManualModel() {
            var channel = new LLMChannel { Id = 14, Name = "MiniMax", Provider = LLMProvider.MiniMax };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddAsync(new ChannelWithModel {
                LLMChannelId = 14,
                ModelName = "legacy-or-account-specific-model",
                IsDeleted = false
            });
            await _context.SaveChangesAsync();

            await _helper.RefreshAllChannel();

            var manualModel = await _context.ChannelsWithModel
                .FirstAsync(m => m.LLMChannelId == 14 && m.ModelName == "legacy-or-account-specific-model");
            Assert.False(manualModel.IsDeleted);
            Assert.Contains(await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 14).ToListAsync(),
                m => m.ModelName == "openai-model1" && !m.IsDeleted);
        }

        [Fact]
        public async Task GetModelsByChannelId_ShouldNotReturnDeletedModels() {
            // Arrange
            var channel = new LLMChannel { Id = 12, Name = "Test", Provider = LLMProvider.OpenAI };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 12, ModelName = "active-model", IsDeleted = false },
                new ChannelWithModel { LLMChannelId = 12, ModelName = "deleted-model", IsDeleted = true }
            });
            await _context.SaveChangesAsync();

            // Act
            var models = await _helper.GetModelsByChannelId(12);

            // Assert
            Assert.Single(models);
            Assert.Contains("active-model", models);
            Assert.DoesNotContain("deleted-model", models);
        }

        [Fact]
        public async Task AddModelWithChannel_ShouldReactivateSoftDeletedModel() {
            // Arrange: model is soft-deleted
            var channel = new LLMChannel { Id = 13, Name = "Test", Provider = LLMProvider.OpenAI };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddAsync(new ChannelWithModel {
                LLMChannelId = 13,
                ModelName = "reactivated-model",
                IsDeleted = true
            });
            await _context.SaveChangesAsync();

            // Act: manually add the model back
            var result = await _helper.AddModelWithChannel(13, new List<string> { "reactivated-model" });

            // Assert: the model should be restored, not duplicated
            Assert.True(result);
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == 13 && m.ModelName == "reactivated-model")
                .ToListAsync();
            Assert.Single(models);
            Assert.False(models[0].IsDeleted);
        }

        [Fact]
        public async Task RefreshAllChannel_ShouldUpdateAllModels() {
            // Arrange
            // Use mocks initialized in Initialize()

            // Mock GeminiService
            var geminiServiceMock = new Mock<GeminiService>(MockBehavior.Strict);
            geminiServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "gemini-model1", "gemini-model2" });

            // Create test channels
            var channels = new[] {
                new LLMChannel { Id = 1, Name = "OpenAI", Provider = LLMProvider.OpenAI },
                new LLMChannel { Id = 2, Name = "Ollama", Provider = LLMProvider.Ollama },
                new LLMChannel { Id = 3, Name = "Gemini", Provider = LLMProvider.Gemini }
            };
            await _context.LLMChannels.AddRangeAsync(channels);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.RefreshAllChannel();

            // Assert
            Assert.Equal(6, result); // 2 models per provider * 3 providers
            var models = await _context.ChannelsWithModel.ToListAsync();
            Assert.Equal(6, models.Count);
        }

        [Fact]
        public async Task RefreshAllChannel_ShouldUpdateAllModels_2() {
            // Arrange
            // Use mocks initialized in Initialize()

            // Create test channels
            var channels = new[] {
                new LLMChannel { Id = 1, Name = "OpenAI", Provider = LLMProvider.OpenAI },
                new LLMChannel { Id = 2, Name = "Ollama", Provider = LLMProvider.Ollama },
                new LLMChannel { Id = 3, Name = "Gemini", Provider = LLMProvider.Gemini }
            };
            await _context.LLMChannels.AddRangeAsync(channels);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.RefreshAllChannel();

            // Assert
            Assert.Equal(6, result); // 2 models per provider * 3 providers
            var models = await _context.ChannelsWithModel.ToListAsync();
            Assert.Equal(6, models.Count);
            Assert.Contains(models, m => m.ModelName == "openai-model1" && m.LLMChannelId == 1);
            Assert.Contains(models, m => m.ModelName == "openai-model2" && m.LLMChannelId == 1);
            Assert.Contains(models, m => m.ModelName == "ollama-model1" && m.LLMChannelId == 2);
            Assert.Contains(models, m => m.ModelName == "ollama-model2" && m.LLMChannelId == 2);
            Assert.Contains(models, m => m.ModelName == "gemini-model1" && m.LLMChannelId == 3);
            Assert.Contains(models, m => m.ModelName == "gemini-model2" && m.LLMChannelId == 3);
        }

        [Fact]
        public async Task AddChannel_ShouldAddModelsForProvider() {
            // Act
            var result = await _helper.AddChannel("Test", "http://test.com", "key", LLMProvider.OpenAI);

            // Assert
            Assert.True(result > 0);
            var channel = await _context.LLMChannels.FindAsync(result);
            Assert.NotNull(channel);
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == result)
                .ToListAsync();
            Assert.Equal(2, models.Count);
        }

        [Fact]
        public async Task AddModelWithChannel_ShouldAddMultipleModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "Test",
                Gateway = "http://test.com",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.AddModelWithChannel(channel.Id, "new1,new2");

            // Assert
            Assert.True(result);
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.Equal(2, models.Count);
        }

        [Fact]
        public async Task RemoveModelFromChannel_ShouldRemoveModel() {
            // Arrange
            var channel = new LLMChannel {
                Name = "Test",
                Gateway = "http://test.com",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddAsync(new ChannelWithModel {
                LLMChannelId = channel.Id,
                ModelName = "test-model"
            });
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.RemoveModelFromChannel(channel.Id, "test-model");

            // Assert
            Assert.True(result);
            var model = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == channel.Id);
            Assert.Null(model);
        }

        [Fact]
        public async Task UpdateChannel_ShouldUpdateProperties() {
            // Arrange
            var channel = new LLMChannel {
                Name = "Old",
                Gateway = "http://old.com",
                ApiKey = "old-key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 0
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.UpdateChannel(
                channel.Id,
                name: "New",
                gateway: "http://new.com",
                apiKey: "new-key",
                provider: LLMProvider.Ollama,
                parallel: 5,
                priority: 2);

            // Assert
            Assert.True(result);
            var updated = await _context.LLMChannels.FindAsync(channel.Id);
            Assert.Equal("New", updated.Name);
            Assert.Equal("http://new.com", updated.Gateway);
            Assert.Equal("new-key", updated.ApiKey);
            Assert.Equal(LLMProvider.Ollama, updated.Provider);
            Assert.Equal(5, updated.Parallel);
            Assert.Equal(2, updated.Priority);
        }

        [Fact]
        public async Task GetChannelById_ShouldReturnCorrectChannel() {
            // Arrange
            var channel = new LLMChannel {
                Id = 1,
                Name = "Test Channel",
                Gateway = "http://test.com",
                ApiKey = "test-key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.GetChannelById(1);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(channel.Id, result.Id);
            Assert.Equal(channel.Name, result.Name);
            Assert.Equal(channel.Gateway, result.Gateway);
            Assert.Equal(channel.Provider, result.Provider);
        }

        [Fact]
        public async Task GetChannelById_ShouldReturnNullForNonExistingId() {
            // Act
            var result = await _helper.GetChannelById(999);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetChannelsByName_ShouldReturnMatchingChannels() {
            // Arrange
            var channels = new[] {
                new LLMChannel { Id = 1, Name = "OpenAI Test", Provider = LLMProvider.OpenAI },
                new LLMChannel { Id = 2, Name = "Ollama Test", Provider = LLMProvider.Ollama },
                new LLMChannel { Id = 3, Name = "Gemini Test", Provider = LLMProvider.Gemini },
                new LLMChannel { Id = 4, Name = "Another", Provider = LLMProvider.OpenAI }
            };
            await _context.LLMChannels.AddRangeAsync(channels);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.GetChannelsByName("Test");

            // Assert
            Assert.Equal(3, result.Count);
            foreach (var c in result) {
                Assert.Contains("Test", c.Name);
            }
        }

        [Fact]
        public async Task GetChannelsByName_ShouldReturnEmptyForNoMatches() {
            // Arrange
            var channels = new[] {
                new LLMChannel { Id = 1, Name = "OpenAI Test", Provider = LLMProvider.OpenAI },
                new LLMChannel { Id = 2, Name = "Ollama Test", Provider = LLMProvider.Ollama }
            };
            await _context.LLMChannels.AddRangeAsync(channels);
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.GetChannelsByName("Nonexistent");

            // Assert
            Assert.Empty(result);
        }

        // ===== Phase 3: Catalog != Entitlement (blueprint §四) =====

        private async Task<LLMApiBinding> SeedChannelWithDefaultBinding(int id, string name, LLMProvider provider,
            string gateway = "http://gw", string? endpoint = null) {
            var channel = new LLMChannel { Id = id, Name = name, Provider = provider, Gateway = gateway };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();
            var binding = new LLMApiBinding {
                LLMChannelId = id,
                Endpoint = endpoint ?? gateway,
                Protocol = LlmProtocol.OpenAIChat,
                AuthProfile = LlmAuthProfile.Bearer,
                IsDefault = true
            };
            await _context.LLMApiBindings.AddAsync(binding);
            await _context.SaveChangesAsync();
            return binding;
        }

        [Fact]
        public async Task RefreshAllChannel_OpenCodeBinding_CreatesNoRowsAndSoftDeletesNothing() {
            // Arrange: OpenCode 默认 binding（opencode.ai/zen/* 空间）——目录不是授权快照，
            // 刷新不得创建行、不得软删/复活任何行（blueprint §四.1/.5）
            const string openCodeEndpoint = "https://opencode.ai/zen/go/v1/chat/completions";
            var binding = await SeedChannelWithDefaultBinding(20, "OpenCode", LLMProvider.OpenAI,
                gateway: openCodeEndpoint, endpoint: openCodeEndpoint);
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 20, ModelName = "ghost-model", IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id },
                new ChannelWithModel { LLMChannelId = 20, ModelName = "gone-model", IsDeleted = true, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id },
                new ChannelWithModel { LLMChannelId = 20, ModelName = "manual-active", IsDeleted = false, AuthorizationSource = AuthorizationSource.Manual },
                new ChannelWithModel { LLMChannelId = 20, ModelName = "manual-gone", IsDeleted = true, AuthorizationSource = AuthorizationSource.Manual }
            });
            await _context.SaveChangesAsync();

            // Act（OpenCode 门禁在取目录前短路，即使 mock 返回模型也不产生任何行）
            var count = await _helper.RefreshAllChannel();

            // Assert：无新增、无软删、无复活
            Assert.Equal(0, count);
            var rows = await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 20).ToListAsync();
            Assert.Equal(4, rows.Count);
            Assert.False(rows.Single(r => r.ModelName == "ghost-model").IsDeleted);
            Assert.True(rows.Single(r => r.ModelName == "gone-model").IsDeleted);
            Assert.False(rows.Single(r => r.ModelName == "manual-active").IsDeleted);
            Assert.True(rows.Single(r => r.ModelName == "manual-gone").IsDeleted);
            Assert.DoesNotContain(rows, r => r.ModelName == "openai-model1");
        }

        [Fact]
        public async Task RefreshAllChannel_FetchFailure_ManualAndDiscoveredRowsUntouched() {
            // Arrange: 目录抓取失败 → 整 channel 跳过，不产生任何创建/软删
            var binding = await SeedChannelWithDefaultBinding(21, "OpenAI", LLMProvider.OpenAI);
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 21, ModelName = "manual-active", IsDeleted = false, AuthorizationSource = AuthorizationSource.Manual },
                new ChannelWithModel { LLMChannelId = 21, ModelName = "disc-active", IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id }
            });
            await _context.SaveChangesAsync();

            _openAIServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ThrowsAsync(new InvalidOperationException("upstream down"));

            // Act
            var count = await _helper.RefreshAllChannel();

            // Assert
            Assert.Equal(0, count);
            var rows = await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 21).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.False(rows.Single(r => r.ModelName == "manual-active").IsDeleted);
            Assert.False(rows.Single(r => r.ModelName == "disc-active").IsDeleted);

            // 恢复 mock 供后续测试使用
            _openAIServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "openai-model1", "openai-model2" });
        }

        [Fact]
        public async Task RefreshAllChannel_ManualRowsNeverSoftDeleted_OnCatalogMissingItems() {
            // Arrange: 目录缺项时 Manual 行永不软删，同 channel 的 Discovered 行照常软删
            var binding = await SeedChannelWithDefaultBinding(22, "OpenAI", LLMProvider.OpenAI);
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 22, ModelName = "manual-ghost", IsDeleted = false, AuthorizationSource = AuthorizationSource.Manual },
                new ChannelWithModel { LLMChannelId = 22, ModelName = "disc-ghost", IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id }
            });
            await _context.SaveChangesAsync();

            // Act（mock 目录只含 openai-model1/openai-model2，两个 ghost 都不在其中）
            await _helper.RefreshAllChannel();

            // Assert
            var manualGhost = await _context.ChannelsWithModel.FirstAsync(m => m.LLMChannelId == 22 && m.ModelName == "manual-ghost");
            Assert.False(manualGhost.IsDeleted);
            var discGhost = await _context.ChannelsWithModel.FirstAsync(m => m.LLMChannelId == 22 && m.ModelName == "disc-ghost");
            Assert.True(discGhost.IsDeleted);
        }

        [Fact]
        public async Task RefreshAllChannel_CaseInsensitiveDedup_NoDuplicate() {
            // Arrange: 已有 Discovered 行 gpt-4o，目录返回 GPT-4O（大小写不同）→ 合并，不新建重复行
            var binding = await SeedChannelWithDefaultBinding(23, "OpenAI", LLMProvider.OpenAI);
            await _context.ChannelsWithModel.AddAsync(new ChannelWithModel {
                LLMChannelId = 23, ModelName = "gpt-4o", IsDeleted = false,
                AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = binding.Id
            });
            await _context.SaveChangesAsync();

            _openAIServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "GPT-4O", "new-model" });

            // Act
            await _helper.RefreshAllChannel();

            // Assert：gpt-4o 只有一行且未删除；new-model 以 Discovered 新建
            var gptRows = await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 23 && m.ModelName == "gpt-4o").ToListAsync();
            Assert.Single(gptRows);
            Assert.False(gptRows[0].IsDeleted);
            var newModel = await _context.ChannelsWithModel.FirstAsync(m => m.LLMChannelId == 23 && m.ModelName == "new-model");
            Assert.Equal(AuthorizationSource.Discovered, newModel.AuthorizationSource);
            Assert.Equal(binding.Id, newModel.ApiBindingId);

            _openAIServiceMock.Setup(s => s.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "openai-model1", "openai-model2" });
        }

        [Fact]
        public async Task RefreshAllChannel_ChannelWithoutDefaultBinding_CreatesExactlyOne() {
            // Arrange: 无任何 binding 的 legacy 渠道（如旧二进制新建），刷新时按 Provider/Gateway 补建恰好一个默认 binding
            var channel = new LLMChannel { Id = 24, Name = "Claude", Provider = LLMProvider.Anthropic, Gateway = "https://api.anthropic.com" };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Act（Anthropic 未注册到 factory mock，服务为 null 也先完成 repair）
            await _helper.RefreshAllChannel();

            // Assert：恰好一个默认 binding，映射与迁移一致
            var bindings = await _context.LLMApiBindings.Where(b => b.LLMChannelId == 24).ToListAsync();
            Assert.Single(bindings);
            Assert.True(bindings[0].IsDefault);
            Assert.Equal("https://api.anthropic.com", bindings[0].Endpoint);
            Assert.Equal(LlmProtocol.AnthropicMessages, bindings[0].Protocol);
            Assert.Equal(LlmAuthProfile.AnthropicApiKey, bindings[0].AuthProfile);
        }

        [Fact]
        public async Task AddModelWithChannel_CaseInsensitiveMerges() {
            // Arrange: 已存在 gpt-4o，管理员添加 GPT-4O → 合并到同一行，不重复（blueprint §七.6）
            var binding = await SeedChannelWithDefaultBinding(25, "OpenAI", LLMProvider.OpenAI);
            await _context.ChannelsWithModel.AddAsync(new ChannelWithModel {
                LLMChannelId = 25, ModelName = "gpt-4o", IsDeleted = false,
                AuthorizationSource = AuthorizationSource.Manual, ApiBindingId = binding.Id
            });
            await _context.SaveChangesAsync();

            // Act
            var result = await _helper.AddModelWithChannel(25, new List<string> { "GPT-4O" });

            // Assert
            Assert.True(result);
            var rows = await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 25).ToListAsync();
            Assert.Single(rows);
            Assert.False(rows[0].IsDeleted);
        }

        [Fact]
        public async Task GetModelsByChannelId_MultiBindingShowsSuffix_SingleBindingPlain() {
            // Arrange: 同一模型跨两个 binding → `model [channel/binding/protocol]`；单 binding 模型保持原名
            var binding1 = await SeedChannelWithDefaultBinding(26, "Test Chan", LLMProvider.OpenAI);
            var binding2 = new LLMApiBinding { LLMChannelId = 26, Endpoint = "http://gw2", Protocol = LlmProtocol.OpenAIResponses, AuthProfile = LlmAuthProfile.Bearer, IsDefault = false };
            await _context.LLMApiBindings.AddAsync(binding2);
            await _context.SaveChangesAsync();
            await _context.ChannelsWithModel.AddRangeAsync(new[] {
                new ChannelWithModel { LLMChannelId = 26, ModelName = "gpt-4o", IsDeleted = false, ApiBindingId = binding1.Id },
                new ChannelWithModel { LLMChannelId = 26, ModelName = "gpt-4o", IsDeleted = false, ApiBindingId = binding2.Id },
                new ChannelWithModel { LLMChannelId = 26, ModelName = "single-model", IsDeleted = false, ApiBindingId = binding1.Id }
            });
            await _context.SaveChangesAsync();

            // Act
            var models = await _helper.GetModelsByChannelId(26);

            // Assert
            Assert.Equal(3, models.Count);
            Assert.Contains($"gpt-4o [Test Chan/{binding1.Id}/OpenAIChat]", models);
            Assert.Contains($"gpt-4o [Test Chan/{binding2.Id}/OpenAIResponses]", models);
            Assert.Contains("single-model", models);
        }

        [Fact]
        public async Task SetDefaultBinding_CreatesDefaultAndMirrorsChannel() {
            // Arrange: 无 binding 的渠道
            var channel = new LLMChannel { Id = 27, Name = "New", Provider = LLMProvider.OpenAI, Gateway = "http://old" };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Act
            var ok = await _helper.SetDefaultBinding(27, "https://new.endpoint/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer);

            // Assert：创建默认 binding + 镜像 Gateway/Provider（旧二进制继续可用，blueprint §七）
            Assert.True(ok);
            var binding = await _context.LLMApiBindings.SingleAsync(b => b.LLMChannelId == 27);
            Assert.True(binding.IsDefault);
            Assert.Equal("https://new.endpoint/v1", binding.Endpoint);
            Assert.Equal(LlmProtocol.OpenAIChat, binding.Protocol);
            var updated = await _context.LLMChannels.FindAsync(27);
            Assert.Equal("https://new.endpoint/v1", updated.Gateway);
            Assert.Equal(LLMProvider.OpenAI, updated.Provider);
        }

        [Fact]
        public async Task SetDefaultBinding_SecondDefaultDemotedWithWarning() {
            // Arrange: 数据异常——渠道已存在两个 IsDefault binding
            var binding1 = await SeedChannelWithDefaultBinding(28, "OpenAI", LLMProvider.OpenAI, gateway: "http://a");
            var binding2 = new LLMApiBinding { LLMChannelId = 28, Endpoint = "http://b", Protocol = LlmProtocol.OpenAIResponses, AuthProfile = LlmAuthProfile.Bearer, IsDefault = true };
            await _context.LLMApiBindings.AddAsync(binding2);
            await _context.SaveChangesAsync();

            // Act
            var ok = await _helper.SetDefaultBinding(28, "http://c", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer);

            // Assert：至多一个 IsDefault（保留 Id 最小者），并记录告警
            Assert.True(ok);
            var bindings = await _context.LLMApiBindings.Where(b => b.LLMChannelId == 28).ToListAsync();
            Assert.Equal(1, bindings.Count(b => b.IsDefault));
            Assert.True(bindings.Single(b => b.IsDefault).Id == Math.Min(binding1.Id, binding2.Id));
            _loggerMock.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task SetModelPreferred_SecondPreferredDemotedWithWarning() {
            // Arrange: 同一模型在两条 binding 上各有行，b1 行已 preferred
            var binding1 = await SeedChannelWithDefaultBinding(29, "OpenAI", LLMProvider.OpenAI);
            var binding2 = new LLMApiBinding { LLMChannelId = 29, Endpoint = "http://gw2", Protocol = LlmProtocol.OpenAIResponses, AuthProfile = LlmAuthProfile.Bearer, IsDefault = false };
            await _context.LLMApiBindings.AddAsync(binding2);
            await _context.SaveChangesAsync();
            var row1 = new ChannelWithModel { LLMChannelId = 29, ModelName = "m", IsDeleted = false, ApiBindingId = binding1.Id, IsPreferred = true };
            var row2 = new ChannelWithModel { LLMChannelId = 29, ModelName = "m", IsDeleted = false, ApiBindingId = binding2.Id, IsPreferred = false };
            await _context.ChannelsWithModel.AddRangeAsync(row1, row2);
            await _context.SaveChangesAsync();

            // Act: 把 preferred 切到 binding2 的行（模型名大小写不同，验证 OIC 匹配）
            var ok = await _helper.SetModelPreferred(29, "M", binding2.Id);

            // Assert：同 channel/model 至多一个 IsPreferred；目标行生效，旧行降级 + 告警
            Assert.True(ok);
            var rows = await _context.ChannelsWithModel.Where(m => m.LLMChannelId == 29).ToListAsync();
            Assert.Equal(1, rows.Count(r => r.IsPreferred));
            Assert.True(rows.Single(r => r.IsPreferred).ApiBindingId == binding2.Id);
            Assert.False(rows.Single(r => r.ApiBindingId == binding1.Id).IsPreferred);
            _loggerMock.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task SetModelPreferred_UnknownRowRejected() {
            // Arrange
            var binding1 = await SeedChannelWithDefaultBinding(31, "OpenAI", LLMProvider.OpenAI);

            // Act: 不存在的 (model, binding) 行 → 拒绝
            var ok = await _helper.SetModelPreferred(31, "nope", binding1.Id);

            // Assert
            Assert.False(ok);
        }

        [Fact]
        public async Task UpdateChannel_GatewayAndProviderChange_SyncsDefaultBinding() {
            // Arrange
            var binding = await SeedChannelWithDefaultBinding(30, "OpenAI", LLMProvider.OpenAI, gateway: "http://old");

            // Act 1: 编辑渠道地址 → 默认 binding Endpoint 同步
            var ok1 = await _helper.UpdateChannel(30, gateway: "http://new");
            Assert.True(ok1);
            var bindingAfter = await _context.LLMApiBindings.FindAsync(binding.Id);
            Assert.Equal("http://new", bindingAfter.Endpoint);

            // Act 2: 编辑渠道类型 → 默认 binding Protocol/Auth 同步
            var ok2 = await _helper.UpdateChannel(30, provider: LLMProvider.ResponsesAPI);
            Assert.True(ok2);
            bindingAfter = await _context.LLMApiBindings.FindAsync(binding.Id);
            Assert.Equal(LlmProtocol.OpenAIResponses, bindingAfter.Protocol);
            Assert.Equal(LlmAuthProfile.Bearer, bindingAfter.AuthProfile);
            Assert.Equal("http://new", bindingAfter.Endpoint);
        }
    }
}
