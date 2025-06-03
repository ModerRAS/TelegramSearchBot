using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;
using TelegramSearchBot.LLM.Infrastructure.Decorators;
using TelegramSearchBot.LLM.Infrastructure.Services;
using TelegramSearchBot.LLM.Tests.Common;
using Xunit;

namespace TelegramSearchBot.LLM.Tests.Infrastructure.Decorators;

public class DecoratorTests
{
    public class 当使用日志装饰器时 : BddTestBase
    {
        private readonly Mock<ILLMService> _mockInnerService = new();
        private readonly Mock<ILogger<LoggingLLMServiceDecorator>> _mockLogger = new();
        private LoggingLLMServiceDecorator _loggingDecorator = null!;
        private LLMRequest _request = null!;
        private LLMResponse? _response;

        protected override Task Given()
        {
            // Given: 我有一个配置好的日志装饰器
            _loggingDecorator = new LoggingLLMServiceDecorator(_mockInnerService.Object, _mockLogger.Object);
            
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
                }
            );

            // And: 内部服务能够成功执行请求
            _mockInnerService
                .Setup(x => x.ExecuteAsync(_request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(LLMResponse.Success(_request.RequestId, _request.Model, "Hello! I'm doing well."));

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我通过日志装饰器执行LLM请求
            _response = await _loggingDecorator.ExecuteAsync(_request);
        }

        protected override Task Then()
        {
            // Then: 应该返回成功的响应
            _response.Should().NotBeNull();
            _response!.IsSuccess.Should().BeTrue();
            _response.Content.Should().Be("Hello! I'm doing well.");

            // And: 应该调用了内部服务
            _mockInnerService.Verify(x => x.ExecuteAsync(_request, It.IsAny<CancellationToken>()), Times.Once);

            // And: 应该记录了开始和完成日志
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("开始执行LLM请求")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("LLM请求执行完成")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该记录详细的执行日志()
        {
            await RunTest();
        }
    }

    public class 当使用工具调用装饰器时 : BddTestBase
    {
        private readonly Mock<ILLMService> _mockInnerService = new();
        private readonly Mock<ILogger<ToolInvocationLLMServiceDecorator>> _mockLogger = new();
        private readonly Mock<ILogger<DefaultToolInvocationService>> _mockToolLogger = new();
        private ToolInvocationLLMServiceDecorator _toolDecorator = null!;
        private DefaultToolInvocationService _toolService = null!;
        private LLMRequest _request = null!;
        private LLMResponse? _response;

        protected override Task Given()
        {
            // Given: 我有一个工具调用服务
            _toolService = new DefaultToolInvocationService(_mockToolLogger.Object);
            
            // And: 注册了一个简单的工具
            var timeToolDefinition = new ToolDefinition(
                Name: "get_time",
                Description: "获取当前时间",
                Parameters: new List<ToolParameter>());

            _toolService.RegisterTool(timeToolDefinition, async _ =>
            {
                return new { current_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            });

            // And: 我有一个配置好的工具调用装饰器
            _toolDecorator = new ToolInvocationLLMServiceDecorator(
                _mockInnerService.Object, 
                _toolService, 
                _mockLogger.Object);
            
            // And: 我有一个LLM请求
            _request = new LLMRequest(
                RequestId: "test-request-123",
                Model: "gpt-3.5-turbo",
                Channel: new LLMChannelConfig(
                    Gateway: "https://api.openai.com/v1",
                    ApiKey: "test-api-key"
                ),
                ChatHistory: new List<LLMMessage>
                {
                    new(LLMRole.User, "现在几点了？")
                }
            );

            // And: 第一次调用返回包含工具调用的响应
            _mockInnerService
                .SetupSequence(x => x.ExecuteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(LLMResponse.Success(_request.RequestId, _request.Model, 
                    "我来帮你查看当前时间。<tool_call>{\"tool_name\":\"get_time\",\"parameters\":{}}</tool_call>"))
                .ReturnsAsync(LLMResponse.Success(_request.RequestId, _request.Model, 
                    "当前时间是2024年1月1日 12:00:00"));

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我通过工具调用装饰器执行LLM请求
            _response = await _toolDecorator.ExecuteAsync(_request);
        }

        protected override Task Then()
        {
            // Then: 应该返回成功的响应
            _response.Should().NotBeNull();
            _response!.IsSuccess.Should().BeTrue();

            // And: 应该调用了内部服务两次（初始请求 + 工具结果处理）
            _mockInnerService.Verify(x => x.ExecuteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

            // And: 应该记录了工具调用相关日志
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("检测到工具调用请求")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该正确处理工具调用()
        {
            await RunTest();
        }
    }

    public class 当组合多个装饰器时 : BddTestBase
    {
        private readonly Mock<ILLMService> _mockInnerService = new();
        private readonly Mock<ILogger<LoggingLLMServiceDecorator>> _mockLogLogger = new();
        private readonly Mock<ILogger<ToolInvocationLLMServiceDecorator>> _mockToolLogger = new();
        private readonly Mock<ILogger<DefaultToolInvocationService>> _mockToolServiceLogger = new();
        private ILLMService _decoratedService = null!;
        private DefaultToolInvocationService _toolService = null!;
        private LLMRequest _request = null!;
        private LLMResponse? _response;

        protected override Task Given()
        {
            // Given: 我有一个工具调用服务
            _toolService = new DefaultToolInvocationService(_mockToolServiceLogger.Object);

            // And: 我有一个组合了多个装饰器的服务
            // 装饰器顺序：核心服务 -> 工具调用装饰器 -> 日志装饰器
            var toolDecorator = new ToolInvocationLLMServiceDecorator(
                _mockInnerService.Object, 
                _toolService, 
                _mockToolLogger.Object);
            
            _decoratedService = new LoggingLLMServiceDecorator(toolDecorator, _mockLogLogger.Object);
            
            // And: 我有一个LLM请求
            _request = new LLMRequest(
                RequestId: "test-request-123",
                Model: "gpt-3.5-turbo",
                Channel: new LLMChannelConfig(
                    Gateway: "https://api.openai.com/v1",
                    ApiKey: "test-api-key"
                ),
                ChatHistory: new List<LLMMessage>
                {
                    new(LLMRole.User, "Hello!")
                }
            );

            // And: 内部服务返回简单响应（无工具调用）
            _mockInnerService
                .Setup(x => x.ExecuteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(LLMResponse.Success(_request.RequestId, _request.Model, "Hello! How can I help you?"));

            return Task.CompletedTask;
        }

        protected override async Task When()
        {
            // When: 我通过组合装饰器执行LLM请求
            _response = await _decoratedService.ExecuteAsync(_request);
        }

        protected override Task Then()
        {
            // Then: 应该返回成功的响应
            _response.Should().NotBeNull();
            _response!.IsSuccess.Should().BeTrue();
            _response.Content.Should().Be("Hello! How can I help you?");

            // And: 应该调用了内部服务
            _mockInnerService.Verify(x => x.ExecuteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Once);

            // And: 应该记录了日志装饰器的日志
            _mockLogLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("开始执行LLM请求")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该正确组合多个装饰器()
        {
            await RunTest();
        }
    }
} 