using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Infrastructure.Factories;
using TelegramSearchBot.LLM.Infrastructure.Services;
using TelegramSearchBot.LLM.Tests.Common;
using Xunit;

namespace TelegramSearchBot.LLM.Tests.Infrastructure.Factories;

/// <summary>
/// 服务工厂测试
/// </summary>
public class 当使用OllamaServiceFactory时 : BddTestBase
{
    private OllamaServiceFactory _factory = null!;
    private Mock<ILogger<OllamaLLMService>> _mockLogger = null!;

    protected override Task Given()
    {
        // Given: 我有一个OllamaServiceFactory实例
        _mockLogger = new Mock<ILogger<OllamaLLMService>>();
        _factory = new OllamaServiceFactory(_mockLogger.Object);

        return Task.CompletedTask;
    }

    protected override Task When()
    {
        // When: (测试在Then中进行)
        return Task.CompletedTask;
    }

    protected override Task Then()
    {
        // Then: 应该支持正确的提供商
        Assert.Equal(LLMProvider.Ollama, _factory.SupportedProvider);
        
        // 并且应该能创建服务
        var service = _factory.CreateService();
        Assert.NotNull(service);
        Assert.IsType<OllamaLLMService>(service);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task 应该正确创建Ollama服务()
    {
        await RunTest();
    }
}

public class 当使用GeminiServiceFactory时 : BddTestBase
{
    private GeminiServiceFactory _factory = null!;
    private Mock<ILogger<GeminiLLMService>> _mockLogger = null!;
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;

    protected override Task Given()
    {
        // Given: 我有一个GeminiServiceFactory实例
        _mockLogger = new Mock<ILogger<GeminiLLMService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _factory = new GeminiServiceFactory(_mockLogger.Object, _mockHttpClientFactory.Object);

        return Task.CompletedTask;
    }

    protected override Task When()
    {
        // When: (测试在Then中进行)
        return Task.CompletedTask;
    }

    protected override Task Then()
    {
        // Then: 应该支持正确的提供商
        Assert.Equal(LLMProvider.Gemini, _factory.SupportedProvider);
        
        // 并且应该能创建服务
        var service = _factory.CreateService();
        Assert.NotNull(service);
        Assert.IsType<GeminiLLMService>(service);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task 应该正确创建Gemini服务()
    {
        await RunTest();
    }
}

public class 当使用LLMServiceFactoryManager管理多个工厂时 : BddTestBase
{
    private LLMServiceFactoryManager _manager = null!;
    private Mock<ILogger<LLMServiceFactoryManager>> _mockLogger = null!;
    private Mock<ILogger<OllamaLLMService>> _mockOllamaLogger = null!;
    private Mock<ILogger<GeminiLLMService>> _mockGeminiLogger = null!;
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;

    protected override Task Given()
    {
        // Given: 我有一个工厂管理器和多个工厂
        _mockLogger = new Mock<ILogger<LLMServiceFactoryManager>>();
        _mockOllamaLogger = new Mock<ILogger<OllamaLLMService>>();
        _mockGeminiLogger = new Mock<ILogger<GeminiLLMService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        
        _manager = new LLMServiceFactoryManager(_mockLogger.Object);

        return Task.CompletedTask;
    }

    protected override Task When()
    {
        // When: 我注册多个工厂
        var ollamaFactory = new OllamaServiceFactory(_mockOllamaLogger.Object);
        var geminiFactory = new GeminiServiceFactory(_mockGeminiLogger.Object, _mockHttpClientFactory.Object);
        
        _manager.RegisterFactory(ollamaFactory);
        _manager.RegisterFactory(geminiFactory);
        
        return Task.CompletedTask;
    }

    protected override Task Then()
    {
        // Then: 应该能获取所有支持的提供商
        var supportedProviders = _manager.GetSupportedProviders().ToList();
        Assert.Contains(LLMProvider.Ollama, supportedProviders);
        Assert.Contains(LLMProvider.Gemini, supportedProviders);
        
        // 并且应该能为每个提供商创建服务
        var ollamaService = _manager.GetService(LLMProvider.Ollama);
        Assert.NotNull(ollamaService);
        Assert.IsType<OllamaLLMService>(ollamaService);
        
        var geminiService = _manager.GetService(LLMProvider.Gemini);
        Assert.NotNull(geminiService);
        Assert.IsType<GeminiLLMService>(geminiService);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task 应该正确管理多个工厂()
    {
        await RunTest();
    }
}

public class 当工厂管理器请求不支持的提供商时 : BddTestBase
{
    private LLMServiceFactoryManager _manager = null!;
    private Mock<ILogger<LLMServiceFactoryManager>> _mockLogger = null!;
    private Exception? _exception;

    protected override Task Given()
    {
        // Given: 我有一个空的工厂管理器
        _mockLogger = new Mock<ILogger<LLMServiceFactoryManager>>();
        _manager = new LLMServiceFactoryManager(_mockLogger.Object);

        return Task.CompletedTask;
    }

    protected override Task When()
    {
        // When: 我尝试获取不支持的提供商服务
        try
        {
            _manager.GetService(LLMProvider.OpenAI); // 未注册的提供商
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
        return Task.CompletedTask;
    }

    protected override Task Then()
    {
        // Then: 应该抛出NotSupportedException
        Assert.NotNull(_exception);
        Assert.IsType<NotSupportedException>(_exception);
        Assert.Contains("不支持的LLM提供商", _exception.Message);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task 应该正确处理不支持的提供商()
    {
        await RunTest();
    }
} 