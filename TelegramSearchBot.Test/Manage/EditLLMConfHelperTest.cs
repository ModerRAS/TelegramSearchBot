using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
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
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Ollama)).Returns(_ollamaServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Gemini)).Returns(_geminiServiceMock.Object);

            _helper = new EditLLMConfHelper(
                _context,
                llmFactoryMock.Object);
            
            _messageExtensionServiceMock.Setup(m => m.AddOrUpdateAsync(It.IsAny<MessageExtension>()))
                .Returns(Task.CompletedTask);
            _geminiServiceMock.Setup(g => g.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "gemini-model1", "gemini-model2" });
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
            foreach (var c in result)
            {
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
    }
}
