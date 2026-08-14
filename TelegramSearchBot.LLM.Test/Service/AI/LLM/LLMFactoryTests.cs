#pragma warning disable CS8602 // Dereference of a possibly null reference
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            var responsesLogger = new Mock<ILogger<OpenAIResponsesService>>();
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
            var responsesService = new OpenAIResponsesService(
                _dbContext, responsesLogger.Object, messageExtensionServiceMock.Object, httpClientFactoryMock.Object);

            var services = new ServiceCollection()
                .AddSingleton(_openAIServiceMock.Object)
                .AddSingleton(_ollamaServiceMock.Object)
                .AddSingleton(_geminiServiceMock.Object)
                .AddSingleton(anthropicServiceMock.Object)
                .AddSingleton(responsesService)
                .BuildServiceProvider();

            _factory = new LLMFactory(
                services,
                _loggerMock.Object);
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
        public void GetLLMService_MiniMax_ReturnsOpenAIService() {
            var service = _factory.GetLLMService(LLMProvider.MiniMax);

            Assert.Same(_openAIServiceMock.Object, service);
        }

        [Fact]
        public void GetLLMService_None_ThrowsKeyNotFound() {
            Assert.Throws<KeyNotFoundException>(() => _factory.GetLLMService(LLMProvider.None));
        }

        // ====================================================================
        // Phase 2：按 binding 协议选择 client（LlmProtocol）
        // ====================================================================

        [Fact]
        public void GetLLMService_Protocol_OpenAIChat_ReturnsOpenAIService() {
            var service = _factory.GetLLMService(LlmProtocol.OpenAIChat);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<OpenAIService>(service);
        }

        [Fact]
        public void GetLLMService_Protocol_OpenAIResponses_ReturnsOpenAIResponsesService() {
            var service = _factory.GetLLMService(LlmProtocol.OpenAIResponses);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<OpenAIResponsesService>(service);
        }

        [Fact]
        public void GetLLMService_Protocol_AnthropicMessages_ReturnsAnthropicService() {
            var service = _factory.GetLLMService(LlmProtocol.AnthropicMessages);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<AnthropicService>(service);
        }

        [Fact]
        public void GetLLMService_Protocol_Ollama_ReturnsOllamaService() {
            var service = _factory.GetLLMService(LlmProtocol.Ollama);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<OllamaService>(service);
        }

        [Fact]
        public void GetLLMService_Protocol_Gemini_ReturnsGeminiService() {
            var service = _factory.GetLLMService(LlmProtocol.Gemini);
            Assert.NotNull(service);
            Assert.IsAssignableFrom<GeminiService>(service);
        }

        [Fact]
        public void GetLLMService_Route_BindingNull_FallsBackToProvider() {
            var channel = new LLMChannel {
                Name = "legacy",
                Gateway = "https://legacy.example",
                ApiKey = "k",
                Provider = LLMProvider.Anthropic,
                Parallel = 1,
                Priority = 1
            };
            var route = new ResolvedLlmRoute(channel, null, new ChannelWithModel { ModelName = "m" });
            var service = _factory.GetLLMService(route);
            Assert.IsAssignableFrom<AnthropicService>(service);
        }

        [Fact]
        public void GetLLMService_Route_BindingNonNull_UsesBindingProtocol() {
            var channel = new LLMChannel {
                Name = "oc",
                Gateway = "https://legacy.example",
                ApiKey = "k",
                Provider = LLMProvider.Anthropic,
                Parallel = 1,
                Priority = 1
            };
            var binding = new LLMApiBinding {
                Id = 7,
                LLMChannelId = channel.Id,
                Endpoint = "https://opencode.ai/zen/v1",
                Protocol = LlmProtocol.OpenAIChat,
                AuthProfile = LlmAuthProfile.Bearer,
                IsDefault = true
            };
            var route = new ResolvedLlmRoute(channel, binding, new ChannelWithModel { ModelName = "m" });
            // 渠道 provider 是 Anthropic，但 binding 协议是 OpenAIChat → 必须选 OpenAIService（绝不按品牌猜）
            var service = _factory.GetLLMService(route);
            Assert.IsAssignableFrom<OpenAIService>(service);
        }

        [Fact]
        public void GeneralAndAgentPaths_ResolveSameRoute_AndSelectSameClient() {
            // 同一模型、同一 channel、双 binding：General 路径（Resolve）与 Agent 路径（ResolveFirst）
            // 必须解析出相同的 binding（IsPreferred 优先于 channel 默认），且 factory 选同一 client。
            var channel = new LLMChannel {
                Name = "oc",
                Gateway = "https://legacy.example",
                ApiKey = "shared-secret",
                Provider = LLMProvider.OpenAI,
                Parallel = 2,
                Priority = 10
            };
            var chatBinding = new LLMApiBinding {
                Id = 1,
                LLMChannelId = channel.Id,
                Endpoint = "https://opencode.ai/zen/v1",
                Protocol = LlmProtocol.OpenAIChat,
                AuthProfile = LlmAuthProfile.Bearer,
                IsDefault = true
            };
            var responsesBinding = new LLMApiBinding {
                Id = 2,
                LLMChannelId = channel.Id,
                Endpoint = "https://opencode.ai/zen/go/v1",
                Protocol = LlmProtocol.OpenAIResponses,
                AuthProfile = LlmAuthProfile.Bearer,
                IsDefault = false
            };
            channel.Bindings.Add(chatBinding);
            channel.Bindings.Add(responsesBinding);
            var chatRow = new ChannelWithModel {
                Id = 1,
                ModelName = "gpt-x",
                LLMChannelId = channel.Id,
                LLMChannel = channel,
                ApiBindingId = chatBinding.Id,
                ApiBinding = chatBinding
            };
            var preferredRow = new ChannelWithModel {
                Id = 2,
                ModelName = "gpt-x",
                LLMChannelId = channel.Id,
                LLMChannel = channel,
                ApiBindingId = responsesBinding.Id,
                ApiBinding = responsesBinding,
                IsPreferred = true
            };
            var rows = new List<ChannelWithModel> { chatRow, preferredRow };

            var generalRoute = LlmRouteResolver.Resolve(channel, "gpt-x", rows, _loggerMock.Object);
            var agentRoute = LlmRouteResolver.ResolveFirst(
                new[] { (channel, rows) }, "gpt-x", _loggerMock.Object);

            Assert.NotNull(generalRoute);
            Assert.NotNull(agentRoute);
            Assert.Equal(generalRoute!.Binding!.Id, agentRoute!.Binding!.Id);
            Assert.Equal(responsesBinding.Id, generalRoute.Binding.Id);
            Assert.Equal(responsesBinding.Endpoint, generalRoute.Binding.Endpoint);
            Assert.Equal(responsesBinding.Protocol, generalRoute.Binding.Protocol);
            Assert.Equal(responsesBinding.AuthProfile, generalRoute.Binding.AuthProfile);
            Assert.Equal("gpt-x", generalRoute.Model.ModelName);

            var generalService = _factory.GetLLMService(generalRoute);
            var agentService = _factory.GetLLMService(agentRoute);
            Assert.Same(generalService, agentService);
            Assert.IsAssignableFrom<OpenAIResponsesService>(generalService);
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
