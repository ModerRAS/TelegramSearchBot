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
    public class LLMFactoryTests {
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<ILogger<LLMFactory>> _loggerMock;
        private readonly DataDbContext _dbContext;
        private readonly Mock<OpenAIService> _openAIServiceMock;
        private readonly Mock<OllamaService> _ollamaServiceMock;
        private readonly Mock<GeminiService> _geminiServiceMock;
        private readonly LLMFactory _factory;

        public LLMFactoryTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new DataDbContext(options);

            _redisMock = new Mock<IConnectionMultiplexer>();
            _loggerMock = new Mock<ILogger<LLMFactory>>();

            var openAILogger = new Mock<ILogger<OpenAIService>>();
            var ollamaLogger = new Mock<ILogger<OllamaService>>();
            var geminiLogger = new Mock<ILogger<GeminiService>>();
            var messageExtensionServiceMock = new Mock<IMessageExtensionService>();
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var serviceProviderMock = new Mock<IServiceProvider>();

            _openAIServiceMock = new Mock<OpenAIService>(
                _dbContext, openAILogger.Object, messageExtensionServiceMock.Object, httpClientFactoryMock.Object);
            _ollamaServiceMock = new Mock<OllamaService>(
                _dbContext, ollamaLogger.Object, serviceProviderMock.Object, httpClientFactoryMock.Object);
            _geminiServiceMock = new Mock<GeminiService>(
                _dbContext, geminiLogger.Object, httpClientFactoryMock.Object);

            _factory = new LLMFactory(
                _redisMock.Object,
                _dbContext,
                _loggerMock.Object,
                _ollamaServiceMock.Object,
                _openAIServiceMock.Object,
                _geminiServiceMock.Object);
        }

        [Fact]
        public void GetLLMService_OpenAI_ReturnsOpenAIService() {
            var service = _factory.GetLLMService(LLMProvider.OpenAI);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<ILLMService>(service);
        }

        [Fact]
        public void GetLLMService_Ollama_ReturnsOllamaService() {
            var service = _factory.GetLLMService(LLMProvider.Ollama);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<ILLMService>(service);
        }

        [Fact]
        public void GetLLMService_Gemini_ReturnsGeminiService() {
            var service = _factory.GetLLMService(LLMProvider.Gemini);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<ILLMService>(service);
        }

        [Fact]
        public void GetLLMService_None_ThrowsKeyNotFound() {
            Assert.Throws<KeyNotFoundException>(() => _factory.GetLLMService(LLMProvider.None));
        }

        [Fact]
        public void ServiceName_ReturnsLLMFactory() {
            Assert.Equal("LLMFactory", _factory.ServiceName);
        }

        [Fact]
        public void Factory_ImplementsILLMFactory() {
            Assert.IsAssignableFrom<ILLMFactory>(_factory);
        }

        [Fact]
        public void Factory_ImplementsIService() {
            Assert.IsAssignableFrom<IService>(_factory);
        }
    }
}
