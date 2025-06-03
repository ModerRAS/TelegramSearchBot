using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.LLM.Application.Services;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;
using TelegramSearchBot.LLM.Tests.Common;
using Xunit;

namespace TelegramSearchBot.LLM.Tests.Application.Services;

public class LLMApplicationServiceTests
{
    public class 当执行LLM请求时 : BddTestBase
    {
        private readonly Mock<ILLMServiceFactoryManager> _mockFactoryManager = new();
        private readonly Mock<ILLMService> _mockLLMService = new();
        private readonly Mock<ILogger<LLMApplicationService>> _mockLogger = new();
        private LLMApplicationService _applicationService = null!;
        private LLMRequest _request = null!;
        private LLMResponse? _response;

        protected override Task Given()
        {
            // Given: 我有一个配置好的LLM应用服务
            _applicationService = new LLMApplicationService(_mockFactoryManager.Object, _mockLogger.Object);
            
            // And: 我有一个有效的LLM请求
            _request = new LLMRequest(
                RequestId: "test-request-123",
                Model: "gpt-3.5-turbo",
                Channel: new LLMChannelConfig(
                    Gateway: "https://api.openai.com/v1",
                    ApiKey: "test-api-key"
                ),
                ChatHistory: new List<LLMMessage>
                {
                    new(LLMRole.User, "Hello, how are you?")
                },
                SystemPrompt: "You are a helpful assistant."
            );

            // And: 工厂管理器能够返回一个LLM服务
            _mockFactoryManager
                .Setup(x => x.GetService(LLMProvider.OpenAI))
                .Returns(_mockLLMService.Object);

            // And: LLM服务能够成功执行请求
            _mockLLMService
                .Setup(x => x.ExecuteAsync(_request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(LLMResponse.Success(_request.RequestId, _request.Model, "Hello! I'm doing well, thank you."));

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我执行LLM请求
            _response = await _applicationService.ExecuteAsync(LLMProvider.OpenAI, _request);
        }

        protected override Task Then()
        {
            // Then: 应该返回成功的响应
            _response.Should().NotBeNull();
            _response!.IsSuccess.Should().BeTrue();
            _response.Content.Should().Be("Hello! I'm doing well, thank you.");
            _response.RequestId.Should().Be(_request.RequestId);
            _response.Model.Should().Be(_request.Model);

            // And: 应该调用了工厂管理器
            _mockFactoryManager.Verify(x => x.GetService(LLMProvider.OpenAI), Times.Once);

            // And: 应该调用了LLM服务
            _mockLLMService.Verify(x => x.ExecuteAsync(_request, It.IsAny<CancellationToken>()), Times.Once);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该成功执行LLM请求()
        {
            await RunTest();
        }
    }

    public class 当LLM请求无效时 : BddTestBase
    {
        private readonly Mock<ILLMServiceFactoryManager> _mockFactoryManager = new();
        private readonly Mock<ILogger<LLMApplicationService>> _mockLogger = new();
        private LLMApplicationService _applicationService = null!;
        private LLMRequest _invalidRequest = null!;
        private LLMResponse? _response;

        protected override Task Given()
        {
            // Given: 我有一个配置好的LLM应用服务
            _applicationService = new LLMApplicationService(_mockFactoryManager.Object, _mockLogger.Object);
            
            // And: 我有一个无效的LLM请求（缺少模型名称）
            _invalidRequest = new LLMRequest(
                RequestId: "test-request-123",
                Model: "", // 无效：空模型名称
                Channel: new LLMChannelConfig(
                    Gateway: "https://api.openai.com/v1",
                    ApiKey: "test-api-key"
                ),
                ChatHistory: new List<LLMMessage>()
            );

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我执行LLM请求
            _response = await _applicationService.ExecuteAsync(LLMProvider.OpenAI, _invalidRequest);
        }

        protected override Task Then()
        {
            // Then: 应该返回失败的响应
            _response.Should().NotBeNull();
            _response!.IsSuccess.Should().BeFalse();
            _response.ErrorMessage.Should().NotBeNullOrEmpty();

            // And: 不应该调用工厂管理器
            _mockFactoryManager.Verify(x => x.GetService(It.IsAny<LLMProvider>()), Times.Never);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该返回验证错误()
        {
            await RunTest();
        }
    }

    public class 当生成嵌入向量时 : BddTestBase
    {
        private readonly Mock<ILLMServiceFactoryManager> _mockFactoryManager = new();
        private readonly Mock<ILLMService> _mockLLMService = new();
        private readonly Mock<ILogger<LLMApplicationService>> _mockLogger = new();
        private LLMApplicationService _applicationService = null!;
        private string _text = null!;
        private string _model = null!;
        private LLMChannelConfig _channel = null!;
        private float[]? _embedding;

        protected override Task Given()
        {
            // Given: 我有一个配置好的LLM应用服务
            _applicationService = new LLMApplicationService(_mockFactoryManager.Object, _mockLogger.Object);
            
            // And: 我有要生成嵌入向量的文本
            _text = "This is a test text for embedding generation.";
            _model = "text-embedding-ada-002";
            _channel = new LLMChannelConfig(
                Gateway: "https://api.openai.com/v1",
                ApiKey: "test-api-key"
            );

            // And: 工厂管理器能够返回一个LLM服务
            _mockFactoryManager
                .Setup(x => x.GetService(LLMProvider.OpenAI))
                .Returns(_mockLLMService.Object);

            // And: LLM服务能够成功生成嵌入向量
            var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
            _mockLLMService
                .Setup(x => x.GenerateEmbeddingAsync(_text, _model, _channel, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedEmbedding);

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我生成嵌入向量
            _embedding = await _applicationService.GenerateEmbeddingAsync(LLMProvider.OpenAI, _text, _model, _channel);
        }

        protected override Task Then()
        {
            // Then: 应该返回有效的嵌入向量
            _embedding.Should().NotBeNull();
            _embedding!.Length.Should().Be(5);
            _embedding[0].Should().Be(0.1f);
            _embedding[4].Should().Be(0.5f);

            // And: 应该调用了LLM服务
            _mockLLMService.Verify(
                x => x.GenerateEmbeddingAsync(_text, _model, _channel, It.IsAny<CancellationToken>()), 
                Times.Once);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该成功生成嵌入向量()
        {
            await RunTest();
        }
    }
} 