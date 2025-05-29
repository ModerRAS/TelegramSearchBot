using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

namespace TelegramSearchBot.Test.Manage {
    [TestClass]
    public class EditLLMConfHelperTest {
        private DataDbContext _context = null!;
        private Mock<IConnectionMultiplexer> _redisMock = null!;
        private Mock<IDatabase> _dbMock = null!;
        private Mock<OpenAIService> _openAIServiceMock = null!;
        private Mock<MessageExtensionService> _messageExtensionServiceMock = null!;
        private Mock<OllamaService> _ollamaServiceMock = null!;
        private Mock<GeminiService> _geminiServiceMock = null!;
        private EditLLMConfHelper _helper = null!;

        [TestInitialize]
        public void Initialize() {
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

        [TestMethod]
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
            Assert.AreEqual(6, result); // 2 models per provider * 3 providers
            var models = await _context.ChannelsWithModel.ToListAsync();
            Assert.AreEqual(6, models.Count);
        }



        [TestMethod]
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
            Assert.AreEqual(6, result); // 2 models per provider * 3 providers
            var models = await _context.ChannelsWithModel.ToListAsync();
            Assert.AreEqual(6, models.Count);
            Assert.IsTrue(models.Any(m => m.ModelName == "openai-model1" && m.LLMChannelId == 1));
            Assert.IsTrue(models.Any(m => m.ModelName == "openai-model2" && m.LLMChannelId == 1));
            Assert.IsTrue(models.Any(m => m.ModelName == "ollama-model1" && m.LLMChannelId == 2));
            Assert.IsTrue(models.Any(m => m.ModelName == "ollama-model2" && m.LLMChannelId == 2));
            Assert.IsTrue(models.Any(m => m.ModelName == "gemini-model1" && m.LLMChannelId == 3));
            Assert.IsTrue(models.Any(m => m.ModelName == "gemini-model2" && m.LLMChannelId == 3));
        }

        [TestMethod]
        public async Task AddChannel_ShouldAddModelsForProvider() {
            // Act
            var result = await _helper.AddChannel("Test", "http://test.com", "key", LLMProvider.OpenAI);

            // Assert
            Assert.IsTrue(result > 0);
            var channel = await _context.LLMChannels.FindAsync(result);
            Assert.IsNotNull(channel);
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == result)
                .ToListAsync();
            Assert.AreEqual(2, models.Count);
        }

        [TestMethod]
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
            Assert.IsTrue(result);
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.AreEqual(2, models.Count);
        }

        [TestMethod]
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
            Assert.IsTrue(result);
            var model = await _context.ChannelsWithModel
                .FirstOrDefaultAsync(m => m.LLMChannelId == channel.Id);
            Assert.IsNull(model);
        }

        [TestMethod]
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
            Assert.IsTrue(result);
            var updated = await _context.LLMChannels.FindAsync(channel.Id);
            Assert.AreEqual("New", updated.Name);
            Assert.AreEqual("http://new.com", updated.Gateway);
            Assert.AreEqual("new-key", updated.ApiKey);
            Assert.AreEqual(LLMProvider.Ollama, updated.Provider);
            Assert.AreEqual(5, updated.Parallel);
            Assert.AreEqual(2, updated.Priority);
        }

        [TestMethod]
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
            Assert.IsNotNull(result);
            Assert.AreEqual(channel.Id, result.Id);
            Assert.AreEqual(channel.Name, result.Name);
            Assert.AreEqual(channel.Gateway, result.Gateway);
            Assert.AreEqual(channel.Provider, result.Provider);
        }

        [TestMethod]
        public async Task GetChannelById_ShouldReturnNullForNonExistingId() {
            // Act
            var result = await _helper.GetChannelById(999);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
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
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.All(c => c.Name.Contains("Test")));
        }

        [TestMethod]
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
            Assert.AreEqual(0, result.Count);
        }
    }
}
