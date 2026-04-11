#pragma warning disable CS8602 // Dereference of a possibly null reference
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class GeneralLLMServiceTests {
        private readonly DataDbContext _dbContext;
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<ILogger<GeneralLLMService>> _loggerMock;
        private readonly Mock<OpenAIService> _openAIServiceMock;
        private readonly Mock<OllamaService> _ollamaServiceMock;
        private readonly Mock<GeminiService> _geminiServiceMock;
        private readonly Mock<ILLMFactory> _factoryMock;
        private readonly GeneralLLMService _service;

        public GeneralLLMServiceTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new DataDbContext(options);

            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1);
            _dbMock.Setup(d => d.StringDecrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(0);
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            _loggerMock = new Mock<ILogger<GeneralLLMService>>();

            var openAILogger = new Mock<ILogger<OpenAIService>>();
            var ollamaLogger = new Mock<ILogger<OllamaService>>();
            var geminiLogger = new Mock<ILogger<GeminiService>>();
            var anthropicLogger = new Mock<ILogger<AnthropicService>>();
            var messageExtensionServiceMock = new Mock<IMessageExtensionService>();
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var serviceProviderMock = new Mock<IServiceProvider>();

            _openAIServiceMock = new Mock<OpenAIService>(
                _dbContext, openAILogger.Object, messageExtensionServiceMock.Object, httpClientFactoryMock.Object);
            _ollamaServiceMock = new Mock<OllamaService>(
                _dbContext, ollamaLogger.Object, serviceProviderMock.Object, httpClientFactoryMock.Object);
            _geminiServiceMock = new Mock<GeminiService>(
                _dbContext, geminiLogger.Object, httpClientFactoryMock.Object);
            var anthropicServiceMock = new Mock<AnthropicService>(
                _dbContext, anthropicLogger.Object, messageExtensionServiceMock.Object, httpClientFactoryMock.Object);

            _factoryMock = new Mock<ILLMFactory>();

            _service = new GeneralLLMService(
                _redisMock.Object,
                _dbContext,
                _loggerMock.Object,
                _ollamaServiceMock.Object,
                _openAIServiceMock.Object,
                _geminiServiceMock.Object,
                anthropicServiceMock.Object,
                _factoryMock.Object);
        }

        [Fact]
        public void ServiceName_ReturnsExpectedName() {
            Assert.Equal("GeneralLLMService", _service.ServiceName);
        }

        [Fact]
        public void Service_ImplementsIGeneralLLMService() {
            Assert.IsAssignableFrom<IGeneralLLMService>(_service);
        }

        [Fact]
        public void Service_ImplementsIService() {
            Assert.IsAssignableFrom<IService>(_service);
        }

        [Fact]
        public async Task GetChannelsAsync_NoModels_ReturnsEmpty() {
            var channels = await _service.GetChannelsAsync("nonexistent-model");
            Assert.Empty(channels);
        }

        [Fact]
        public async Task GetChannelsAsync_WithModel_ReturnsOrderedChannels() {
            // Arrange
            var channel1 = new LLMChannel {
                Name = "ch1",
                Gateway = "gw1",
                ApiKey = "key1",
                Provider = LLMProvider.OpenAI,
                Parallel = 2,
                Priority = 1
            };
            var channel2 = new LLMChannel {
                Name = "ch2",
                Gateway = "gw2",
                ApiKey = "key2",
                Provider = LLMProvider.OpenAI,
                Parallel = 3,
                Priority = 10
            };
            _dbContext.LLMChannels.AddRange(channel1, channel2);
            await _dbContext.SaveChangesAsync();

            _dbContext.ChannelsWithModel.AddRange(
                new ChannelWithModel { ModelName = "gpt-4", LLMChannelId = channel1.Id },
                new ChannelWithModel { ModelName = "gpt-4", LLMChannelId = channel2.Id }
            );
            await _dbContext.SaveChangesAsync();

            // Act
            var channels = await _service.GetChannelsAsync("gpt-4");

            // Assert
            Assert.Equal(2, channels.Count);
            Assert.Equal("ch2", channels[0].Name); // Higher priority first
        }

        [Fact]
        public async Task ExecAsync_NoModelConfigured_YieldsNoResults() {
            // Arrange - no group settings configured
            var message = new TelegramSearchBot.Model.Data.Message {
                Content = "test",
                GroupId = 123,
                MessageId = 1,
                FromUserId = 1
            };

            // Act
            var results = new List<string>();
            await foreach (var r in _service.ExecAsync(message, 123)) {
                results.Add(r);
            }

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task GetAvailableCapacityAsync_NoChannels_ReturnsZero() {
            var capacity = await _service.GetAvailableCapacityAsync("nonexistent-model");
            Assert.Equal(0, capacity);
        }

        [Fact]
        public async Task GetAvailableCapacityAsync_WithChannels_ReturnsCapacity() {
            // Arrange
            var channel = new LLMChannel {
                Name = "ch1",
                Gateway = "gw1",
                ApiKey = "key1",
                Provider = LLMProvider.OpenAI,
                Parallel = 5,
                Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            _dbContext.ChannelsWithModel.Add(
                new ChannelWithModel { ModelName = "gpt-4", LLMChannelId = channel.Id }
            );
            await _dbContext.SaveChangesAsync();

            // Redis returns 0 for semaphore (no current usage)
            _dbMock.Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains("semaphore")),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Act
            var capacity = await _service.GetAvailableCapacityAsync("gpt-4");

            // Assert
            Assert.Equal(5, capacity);
        }

        [Fact]
        public async Task GetAltPhotoAvailableCapacityAsync_DefaultModel_ReturnsCapacity() {
            // With no config, uses "gemma3:27b" as default - no channels configured
            var capacity = await _service.GetAltPhotoAvailableCapacityAsync();
            Assert.Equal(0, capacity);
        }

        [Fact]
        public async Task GenerateEmbeddingsAsync_NoChannels_ReturnsEmpty() {
            var result = await _service.GenerateEmbeddingsAsync("test text", CancellationToken.None);
            Assert.Empty(result);
        }

        [Fact]
        public async Task AnalyzeImageAsync_NoChannels_ReturnsErrorMessage() {
            var result = await _service.AnalyzeImageAsync("/tmp/test.jpg", 123, CancellationToken.None);
            Assert.StartsWith("Error:", result);
        }
    }
}
