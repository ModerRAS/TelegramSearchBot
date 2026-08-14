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

        [Fact]
        public async Task AnalyzeImageAsync_WithCustomPrompt_ForwardsPromptToProvider() {
            var providerMock = new Mock<ILLMService>();
            var channel = new LLMChannel {
                Name = "vision-channel",
                Gateway = "https://example.com",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };

            providerMock
                .Setup(s => s.AnalyzeImageAsync("image.jpg", "vision-model", channel, GeneralLLMService.DefaultVisionOcrPrompt))
                .ReturnsAsync("recognized text");

            var results = new List<string>();
            await foreach (var result in _service.AnalyzeImageAsync(
                "image.jpg",
                123,
                "vision-model",
                providerMock.Object,
                channel,
                GeneralLLMService.DefaultVisionOcrPrompt,
                CancellationToken.None)) {
                results.Add(result);
            }

            Assert.Single(results);
            Assert.Equal("recognized text", results[0]);
        }

        // ====================================================================
        // Phase 2：确定性路由解析（LlmRouteResolver，General 与 Agent 路径共用）
        // ====================================================================

        private static LLMChannel CreateChannel(int id, string gateway, LLMProvider provider, int priority, int parallel = 1) {
            return new LLMChannel {
                Id = id,
                Name = $"ch{id}",
                Gateway = gateway,
                ApiKey = "channel-key",
                Provider = provider,
                Priority = priority,
                Parallel = parallel
            };
        }

        private static LLMApiBinding CreateBinding(int id, int channelId, string endpoint, LlmProtocol protocol, LlmAuthProfile auth, bool isDefault) {
            return new LLMApiBinding {
                Id = id,
                LLMChannelId = channelId,
                Endpoint = endpoint,
                Protocol = protocol,
                AuthProfile = auth,
                IsDefault = isDefault
            };
        }

        [Fact]
        public void Resolve_SameModelTwoBindings_DefaultBindingPicked() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var bDefault = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            var bOther = CreateBinding(2, 1, "https://zen/go/v1", LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer, isDefault: false);
            channel.Bindings.Add(bDefault);
            channel.Bindings.Add(bOther);
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = bDefault.Id, ApiBinding = bDefault },
                new() { Id = 2, ModelName = "m", LLMChannelId = 1, ApiBindingId = bOther.Id, ApiBinding = bOther }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(bDefault.Id, route!.Binding!.Id);
            Assert.False(route.IsLegacyFallback);
        }

        [Fact]
        public void Resolve_IsPreferred_OverridesChannelDefault() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var bDefault = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            var bPreferred = CreateBinding(2, 1, "https://zen/go/v1", LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer, isDefault: false);
            channel.Bindings.Add(bDefault);
            channel.Bindings.Add(bPreferred);
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = bDefault.Id, ApiBinding = bDefault },
                new() { Id = 2, ModelName = "m", LLMChannelId = 1, ApiBindingId = bPreferred.Id, ApiBinding = bPreferred, IsPreferred = true }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(bPreferred.Id, route!.Binding!.Id);
            Assert.Equal(LlmProtocol.OpenAIResponses, route.Binding.Protocol);
        }

        [Fact]
        public void Resolve_TwoIsDefaultBindings_StableOrderByBindingIdAndWarns() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var b1 = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            var b2 = CreateBinding(2, 1, "https://zen/go/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            channel.Bindings.Add(b1);
            channel.Bindings.Add(b2);
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = b1.Id, ApiBinding = b1 },
                new() { Id = 2, ModelName = "m", LLMChannelId = 1, ApiBindingId = b2.Id, ApiBinding = b2 }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(b1.Id, route!.Binding!.Id); // 稳定排序：最小 binding.Id 胜出，不 throw
            AssertLogWarningContains("IsDefault");
        }

        [Fact]
        public void Resolve_TwoIsPreferredRows_StableOrderAndWarns() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var b1 = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            var b2 = CreateBinding(2, 1, "https://zen/go/v1", LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer, isDefault: false);
            channel.Bindings.Add(b1);
            channel.Bindings.Add(b2);
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = b1.Id, ApiBinding = b1, IsPreferred = true },
                new() { Id = 2, ModelName = "m", LLMChannelId = 1, ApiBindingId = b2.Id, ApiBinding = b2, IsPreferred = true }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(b1.Id, route!.Binding!.Id); // 稳定排序：最小 binding.Id 胜出，不 throw
            AssertLogWarningContains("IsPreferred");
        }

        [Fact]
        public void Resolve_NullApiBindingId_InterpretsAsChannelDefault() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var bDefault = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            channel.Bindings.Add(bDefault);
            // 旧二进制写入的 legacy 行：ApiBindingId == null → 解释为渠道默认 binding
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = null }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(bDefault.Id, route!.Binding!.Id);
            Assert.Equal("https://zen/v1", route.Binding.Endpoint);
            Assert.False(route.IsLegacyFallback);
        }

        [Fact]
        public void Resolve_NoDefaultBinding_LegacyFallbackAndWarns() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.Anthropic, 10);
            var rows = new List<ChannelWithModel> {
                new() { Id = 1, ModelName = "m", LLMChannelId = 1, ApiBindingId = null }
            };

            var route = LlmRouteResolver.Resolve(channel, "m", rows, _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Null(route!.Binding);
            Assert.True(route.IsLegacyFallback);
            Assert.Equal(LLMProvider.Anthropic, route.Channel.Provider); // 回退 legacy Provider/Gateway
            AssertLogWarningContains("临时回退");
        }

        [Fact]
        public void ResolveFirst_ChannelPriorityDesc_AcrossChannels() {
            var lowChannel = CreateChannel(1, "https://low", LLMProvider.OpenAI, priority: 1);
            var bLow = CreateBinding(1, 1, "https://low/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            lowChannel.Bindings.Add(bLow);
            var highChannel = CreateChannel(2, "https://high", LLMProvider.OpenAI, priority: 10);
            var bHigh = CreateBinding(2, 2, "https://high/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            highChannel.Bindings.Add(bHigh);

            var candidates = new List<(LLMChannel, List<ChannelWithModel>)> {
                (highChannel, new List<ChannelWithModel> { new() { Id = 1, ModelName = "m", LLMChannelId = 2, ApiBindingId = bHigh.Id, ApiBinding = bHigh } }),
                (lowChannel, new List<ChannelWithModel> { new() { Id = 2, ModelName = "m", LLMChannelId = 1, ApiBindingId = bLow.Id, ApiBinding = bLow } })
            };

            var route = LlmRouteResolver.ResolveFirst(candidates, "m", _loggerMock.Object);

            Assert.NotNull(route);
            Assert.Equal(highChannel.Id, route!.Channel.Id);
            Assert.Equal(bHigh.Id, route.Binding!.Id);
        }

        [Fact]
        public void LlmBindingSupport_Endpoint_FromBindingWhenPresent() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            var binding = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);

            Assert.Equal("https://zen/v1", LlmBindingSupport.ResolveEndpoint(channel, binding));
            Assert.Equal("https://legacy", LlmBindingSupport.ResolveEndpoint(channel, null));
        }

        [Fact]
        public void LlmBindingSupport_AuthProfile_Isolation() {
            var channel = CreateChannel(1, "https://legacy", LLMProvider.OpenAI, 10);
            channel.ApiKey = "shared-secret";

            // Bearer：OpenAI SDK 原生发 Authorization: Bearer <key>；AnthropicApiKey：Anthropic SDK 原生发 x-api-key <key>。
            // 两者都从 channel 取共享 key；None 走无 key 路径（keyless）。
            var bearer = CreateBinding(1, 1, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            var anthropic = CreateBinding(2, 1, "https://zen/v1", LlmProtocol.AnthropicMessages, LlmAuthProfile.AnthropicApiKey, isDefault: false);
            var none = CreateBinding(3, 1, "http://localhost:11434", LlmProtocol.Ollama, LlmAuthProfile.None, isDefault: false);

            Assert.Equal("shared-secret", LlmBindingSupport.ResolveApiKey(channel, bearer));
            Assert.Equal("shared-secret", LlmBindingSupport.ResolveApiKey(channel, anthropic));
            Assert.Equal(string.Empty, LlmBindingSupport.ResolveApiKey(channel, none));
            // 无 binding（legacy）时保持旧行为：取 channel key
            Assert.Equal("shared-secret", LlmBindingSupport.ResolveApiKey(channel, null));
        }

        [Fact]
        public async Task ExecOperationAsync_UsesResolvedRouteBinding_AndPassesBindingToService() {
            var channel = CreateChannel(11, "https://legacy", LLMProvider.OpenAI, priority: 10);
            channel.ApiKey = "shared-secret";
            var bDefault = CreateBinding(21, 11, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            channel.Bindings.Add(bDefault);
            _dbContext.LLMChannels.Add(channel);
            _dbContext.LLMApiBindings.Add(bDefault);
            _dbContext.ChannelsWithModel.Add(new ChannelWithModel {
                Id = 31,
                ModelName = "m",
                LLMChannelId = 11,
                ApiBindingId = bDefault.Id,
                ApiBinding = bDefault
            });
            await _dbContext.SaveChangesAsync();

            var serviceMock = new Mock<ILLMService>();
            serviceMock.Setup(s => s.IsHealthyAsync(It.IsAny<LLMChannel>(), It.IsAny<LLMApiBinding>()))
                .ReturnsAsync(true);
            serviceMock.Setup(s => s.ExecAsync(
                    It.IsAny<Message>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<LLMChannel>(),
                    It.IsAny<LLMApiBinding>(), It.IsAny<LlmExecutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyStringStream());
            _factoryMock.Setup(f => f.GetLLMService(It.IsAny<ResolvedLlmRoute>())).Returns(serviceMock.Object);

            var results = new List<string>();
            var message = new TelegramSearchBot.Model.Data.Message { Content = "hi", GroupId = 123, MessageId = 1, FromUserId = 1 };
            await foreach (var r in _service.ExecOperationAsync(
                (svc, ch, b, ct) => svc.ExecAsync(message, 123, "m", ch, b, new LlmExecutionContext(), ct),
                "m")) {
                results.Add(r);
            }

            _factoryMock.Verify(f => f.GetLLMService(It.Is<ResolvedLlmRoute>(r => r.Channel.Id == 11 && r.Binding != null && r.Binding.Id == bDefault.Id)), Times.Once);
            serviceMock.Verify(s => s.ExecAsync(
                It.IsAny<Message>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<LLMChannel>(),
                It.Is<LLMApiBinding>(b => b.Id == bDefault.Id), It.IsAny<LlmExecutionContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecOperationAsync_ChannelPriorityDesc_Respected() {
            var lowChannel = CreateChannel(12, "https://low", LLMProvider.OpenAI, priority: 1);
            var bLow = CreateBinding(22, 12, "https://low/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            lowChannel.Bindings.Add(bLow);
            var highChannel = CreateChannel(13, "https://high", LLMProvider.OpenAI, priority: 10);
            var bHigh = CreateBinding(23, 13, "https://high/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            highChannel.Bindings.Add(bHigh);
            _dbContext.LLMChannels.AddRange(lowChannel, highChannel);
            _dbContext.LLMApiBindings.AddRange(bLow, bHigh);
            _dbContext.ChannelsWithModel.AddRange(
                new ChannelWithModel { Id = 32, ModelName = "m", LLMChannelId = 12, ApiBindingId = bLow.Id, ApiBinding = bLow },
                new ChannelWithModel { Id = 33, ModelName = "m", LLMChannelId = 13, ApiBindingId = bHigh.Id, ApiBinding = bHigh });
            await _dbContext.SaveChangesAsync();

            var serviceMock = new Mock<ILLMService>();
            serviceMock.Setup(s => s.IsHealthyAsync(It.IsAny<LLMChannel>(), It.IsAny<LLMApiBinding>()))
                .ReturnsAsync(true);
            serviceMock.Setup(s => s.ExecAsync(
                    It.IsAny<Message>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<LLMChannel>(),
                    It.IsAny<LLMApiBinding>(), It.IsAny<LlmExecutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyStringStream());
            _factoryMock.Setup(f => f.GetLLMService(It.IsAny<ResolvedLlmRoute>())).Returns(serviceMock.Object);

            var message = new TelegramSearchBot.Model.Data.Message { Content = "hi", GroupId = 123, MessageId = 1, FromUserId = 1 };
            await foreach (var _ in _service.ExecOperationAsync(
                (svc, ch, b, ct) => svc.ExecAsync(message, 123, "m", ch, b, new LlmExecutionContext(), ct),
                "m")) {
            }

            // 高优先级渠道先被选中；成功后低优先级渠道不再被访问
            _factoryMock.Verify(f => f.GetLLMService(It.Is<ResolvedLlmRoute>(r => r.Channel.Id == highChannel.Id && r.Binding!.Id == bHigh.Id)), Times.Once);
            _factoryMock.Verify(f => f.GetLLMService(It.Is<ResolvedLlmRoute>(r => r.Channel.Id == lowChannel.Id)), Times.Never);
        }

        [Fact]
        public async Task ResumeFromSnapshotAsync_ResolvesRouteAndPassesBinding() {
            var channel = CreateChannel(14, "https://legacy", LLMProvider.OpenAI, priority: 10);
            channel.ApiKey = "shared-secret";
            var bDefault = CreateBinding(24, 14, "https://zen/v1", LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer, isDefault: true);
            channel.Bindings.Add(bDefault);
            _dbContext.LLMChannels.Add(channel);
            _dbContext.LLMApiBindings.Add(bDefault);
            _dbContext.ChannelsWithModel.Add(new ChannelWithModel {
                Id = 34,
                ModelName = "m",
                LLMChannelId = 14,
                ApiBindingId = bDefault.Id,
                ApiBinding = bDefault
            });
            await _dbContext.SaveChangesAsync();

            var serviceMock = new Mock<ILLMService>();
            serviceMock.Setup(s => s.ResumeFromSnapshotAsync(
                    It.IsAny<LlmContinuationSnapshot>(), It.IsAny<LLMChannel>(), It.IsAny<LLMApiBinding>(),
                    It.IsAny<LlmExecutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(EmptyStringStream());
            _factoryMock.Setup(f => f.GetLLMService(It.IsAny<ResolvedLlmRoute>())).Returns(serviceMock.Object);

            var snapshot = new LlmContinuationSnapshot {
                SnapshotId = "s1",
                ChannelId = 14,
                ModelName = "m",
                Provider = "OpenAI",
                ChatId = 123,
                UserId = 1,
                OriginalMessageId = 1
            };
            var results = new List<string>();
            await foreach (var r in _service.ResumeFromSnapshotAsync(snapshot, new LlmExecutionContext())) {
                results.Add(r);
            }

            _factoryMock.Verify(f => f.GetLLMService(It.Is<ResolvedLlmRoute>(r => r.Channel.Id == 14 && r.Binding != null && r.Binding.Id == bDefault.Id)), Times.Once);
            serviceMock.Verify(s => s.ResumeFromSnapshotAsync(
                It.IsAny<LlmContinuationSnapshot>(), It.IsAny<LLMChannel>(),
                It.Is<LLMApiBinding>(b => b.Id == bDefault.Id), It.IsAny<LlmExecutionContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static async IAsyncEnumerable<string> EmptyStringStream() {
            yield break;
        }

        private void AssertLogWarningContains(string fragment) {
            _loggerMock.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains(fragment)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }
    }
}
