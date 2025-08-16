# Microsoft.Extensions.AI 替换现有 LLM 实现规划

## 1. 现状分析

### 当前 LLM 架构
- **接口层**: `ILLMService` 定义统一接口
- **工厂模式**: `LLMFactory` 管理多个 LLM 服务
- **具体实现**: 
  - `OpenAIService` - 使用 OpenAI .NET SDK (2.2.0)
  - `OllamaService` - 使用 OllamaSharp (5.3.3)
  - `GeminiService` - 使用 Google.GenerativeAI (3.0.0)
- **统一入口**: `GeneralLLMService` 提供负载均衡和故障转移
- **配置管理**: 数据库存储 LLM 通道和模型配置

### 核心功能
- 文本对话 (流式输出)
- 向量嵌入生成
- 图像分析
- 模型健康检查
- 并发控制和优先级调度
- 多通道负载均衡

## 2. Microsoft.Extensions.AI 优势分析

### 核心优势
1. **统一抽象**: 提供标准化的 AI 服务接口
2. **依赖注入原生支持**: 与 .NET 生态系统深度集成
3. **可扩展性**: 易于添加新的 AI 服务提供商
4. **工具调用支持**: 内置 function calling 支持
5. **遥测和日志**: 与 OpenTelemetry 集成
6. **异步流式**: 原生支持流式输出

### 架构改进
- 简化服务注册和配置
- 统一的错误处理和重试机制
- 更好的性能优化
- 标准化的请求/响应模型

## 3. 替换可行性评估

### ✅ 高度可行的部分
1. **文本对话**: `IChatClient` 接口完全匹配需求
2. **依赖注入**: 与现有 DI 容器完美集成
3. **配置管理**: 可与现有配置系统结合
4. **流式输出**: 原生支持异步流

### ⚠️ 需要适配的部分
1. **向量嵌入**: 需要使用 `IEmbeddingGenerator` 接口
2. **图像分析**: 需要专门的图像处理扩展
3. **并发控制**: 需要重新实现现有的 Redis 信号量机制
4. **健康检查**: 需要适配现有的健康检查机制

### ❌ 潜在挑战
1. **多租户配置**: 现有的数据库配置管理需要适配
2. **负载均衡**: 需要重新实现多通道负载均衡逻辑
3. **故障转移**: 需要实现新的故障转移机制

## 4. 详细替换计划

### 阶段 1: 基础设施搭建 (1-2 周)

#### 4.1 包依赖升级
```xml
<!-- 新增包引用 -->
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0-preview.*" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.0.0-preview.*" />
```

#### 4.2 核心接口定义
```csharp
// 新的统一接口，基于 Microsoft.Extensions.AI
public interface IMicrosoftLLMService : IDisposable
{
    IChatClient GetChatClient(LLMChannel channel);
    IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LLMChannel channel);
    Task<bool> IsHealthyAsync(LLMChannel channel);
}
```

#### 4.3 服务注册配置
```csharp
// 在 ServiceCollectionExtension 中添加
public static IServiceCollection AddMicrosoftLLMServices(this IServiceCollection services)
{
    services.AddChatClient(builder => builder
        .UseFunctionInvocation()
        .UseOpenTelemetry()
        .UseLogging());
    
    return services;
}
```

### 阶段 2: 核心服务实现 (2-3 周)

#### 4.4 Microsoft LLM 服务工厂
```csharp
[Injectable(ServiceLifetime.Singleton)]
public class MicrosoftLLMFactory : IMicrosoftLLMService
{
    private readonly Dictionary<LLMProvider, Func<LLMChannel, IChatClient>> _chatClientFactories;
    private readonly Dictionary<LLMProvider, Func<LLMChannel, IEmbeddingGenerator<string, Embedding<float>>>> _embeddingGeneratorFactories;
    
    public MicrosoftLLMFactory(IServiceProvider serviceProvider)
    {
        // 初始化工厂方法
        _chatClientFactories = new()
        {
            [LLMProvider.OpenAI] = channel => CreateOpenAIClient(channel),
            [LLMProvider.Ollama] = channel => CreateOllamaClient(channel),
            [LLMProvider.Gemini] = channel => CreateGeminiClient(channel)
        };
    }
    
    public IChatClient GetChatClient(LLMChannel channel)
    {
        return _chatClientFactories[channel.Provider](channel);
    }
    
    // ... 其他实现
}
```

#### 4.5 统一的 LLM 服务适配器
```csharp
[Injectable(ServiceLifetime.Scoped)]
public class UnifiedLLMService : ILLMService
{
    private readonly IMicrosoftLLMService _microsoftLLMService;
    private readonly GeneralLLMService _generalLLMService; // 保持现有逻辑
    
    public UnifiedLLMService(
        IMicrosoftLLMService microsoftLLMService,
        GeneralLLMService generalLLMService)
    {
        _microsoftLLMService = microsoftLLMService;
        _generalLLMService = generalLLMService;
    }
    
    public async IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatClient = _microsoftLLMService.GetChatClient(channel);
        
        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage(message.Content)
        };
        
        await foreach (var update in chatClient.CompleteStreamingAsync(chatMessages, null, cancellationToken))
        {
            yield return update.ContentUpdate;
        }
    }
    
    // ... 其他接口实现
}
```

### 阶段 3: 高级功能适配 (1-2 周)

#### 4.6 向量嵌入服务适配
```csharp
public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
{
    var embeddingGenerator = _microsoftLLMService.GetEmbeddingGenerator(channel);
    var embeddings = await embeddingGenerator.GenerateEmbeddingsAsync(new[] { text });
    return embeddings[0].Vector.ToArray();
}
```

#### 4.7 图像分析服务适配
```csharp
public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
{
    var chatClient = _microsoftLLMService.GetChatClient(channel);
    
    using var stream = File.OpenRead(photoPath);
    var imageContent = new ImageContent(stream, "image/jpeg");
    
    var response = await chatClient.CompleteAsync([
        new UserChatMessage("请分析这张图片的内容"),
        new UserChatMessage(imageContent)
    ]);
    
    return response.Message.Text;
}
```

#### 4.8 并发控制和负载均衡
```csharp
// 基于现有 Redis 机制，适配新的服务接口
public async IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(
    Func<IMicrosoftLLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation,
    string modelName,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // 保持现有的 Redis 信号量逻辑
    // 替换服务调用为新的 Microsoft.Extensions.AI 接口
}
```

### 阶段 4: 渐进式迁移 (2-3 周)

#### 4.9 特性开关
```csharp
public class LLMFeatureFlags
{
    public const string UseMicrosoftExtensionsAI = "LLM:UseMicrosoftExtensionsAI";
    public const string EnableStreamingOptimization = "LLM:EnableStreamingOptimization";
    public const string EnableEmbeddingCaching = "LLM:EnableEmbeddingCaching";
}
```

#### 4.10 混合模式运行
```csharp
[Injectable(ServiceLifetime.Scoped)]
public class HybridLLMService : ILLMService
{
    private readonly IMicrosoftLLMService _microsoftLLMService;
    private readonly ILLMFactory _legacyLLMFactory;
    private readonly IAppConfigurationService _configService;
    
    public async IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var useNewImplementation = await _configService.GetBoolAsync(LLMFeatureFlags.UseMicrosoftExtensionsAI, false);
        
        if (useNewImplementation)
        {
            var chatClient = _microsoftLLMService.GetChatClient(channel);
            // 使用新的实现
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            // 使用现有实现
        }
    }
}
```

### 阶段 5: 测试和优化 (1-2 周)

#### 4.11 性能测试
- 流式响应延迟对比
- 并发处理能力测试
- 内存使用情况分析
- 错误恢复能力测试

#### 4.12 兼容性测试
- 现有配置数据兼容性
- API 响应格式一致性
- 错误处理机制验证

## 5. 风险评估和缓解措施

### 主要风险
1. **API 兼容性**: Microsoft.Extensions.AI 可能处于预览阶段
2. **功能缺失**: 某些特定功能可能需要自定义实现
3. **性能回归**: 新实现可能存在性能问题
4. **配置复杂性**: 现有配置需要适配

### 缓解措施
1. **渐进式迁移**: 使用特性开关控制新旧实现
2. **充分测试**: 完整的单元测试和集成测试
3. **监控和回滚**: 实时监控和快速回滚机制
4. **文档和培训**: 更新文档和团队培训

## 6. 时间线估算

| 阶段 | 时间 | 主要任务 |
|------|------|----------|
| 阶段 1 | 1-2 周 | 基础设施搭建，包依赖升级 |
| 阶段 2 | 2-3 周 | 核心服务实现，基础功能适配 |
| 阶段 3 | 1-2 周 | 高级功能适配，优化 |
| 阶段 4 | 2-3 周 | 渐进式迁移，混合模式 |
| 阶段 5 | 1-2 周 | 测试，优化，部署 |
| **总计** | **7-12 周** | **完整替换** |

## 7. 预期收益

### 技术收益
1. **代码简化**: 减少自定义抽象层
2. **性能提升**: 原生优化和更好的资源管理
3. **可维护性**: 标准化的接口和模式
4. **扩展性**: 更容易添加新的 AI 服务

### 业务收益
1. **稳定性**: 更好的错误处理和恢复机制
2. **功能增强**: 原生支持工具调用等高级功能
3. **成本优化**: 更好的资源利用和性能
4. **未来兼容**: 与 .NET 生态系统保持同步

## 8. 下一步行动

1. **创建概念验证**: 为单个 LLM 服务创建 POC
2. **性能基准测试**: 对比新旧实现的性能
3. **详细设计文档**: 完善架构设计
4. **团队培训**: Microsoft.Extensions.AI 相关技术培训
5. **制定迁移策略**: 确定具体的迁移步骤和时间表

## 9. 详细重构顺序和实施步骤

### 9.1 重构原则
1. **渐进式迁移**: 保持系统稳定运行，逐步替换组件
2. **向后兼容**: 新实现必须兼容现有 API 接口
3. **特性开关**: 每个阶段都要有回滚机制
4. **充分测试**: 每个步骤都要有完整的测试覆盖

### 9.2 详细实施步骤

#### **第 1 周：环境准备和基础架构**

##### 步骤 1.1: 创建实验分支
```bash
git checkout -b feature/microsoft-extensions-ai-migration
git push origin feature/microsoft-extensions-ai-migration
```

##### 步骤 1.2: 升级项目依赖
```xml
<!-- 在 TelegramSearchBot.csproj 中添加 -->
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0-preview.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.0.0-preview.1" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="9.0.0-preview.1" />
```

##### 步骤 1.3: 创建基础接口和抽象
```csharp
// 创建 TelegramSearchBot/Interface/AI/LLM/IMicrosoftLLMService.cs
public interface IMicrosoftLLMService : IDisposable
{
    IChatClient GetChatClient(LLMChannel channel);
    IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LLMChannel channel);
    Task<bool> IsHealthyAsync(LLMChannel channel);
    Task<IEnumerable<string>> GetAllModels(LLMChannel channel);
    Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);
}

// 创建 TelegramSearchBot/Interface/AI/LLM/IMicrosoftLLMFactory.cs
public interface IMicrosoftLLMFactory
{
    IMicrosoftLLMService CreateLLMService(LLMProvider provider);
}
```

##### 步骤 1.4: 添加配置项
```csharp
// 在 Model/Data/AppConfigurationItem 中添加配置常量
public class LLMConfigurationKeys
{
    public const string UseMicrosoftExtensionsAI = "LLM:UseMicrosoftExtensionsAI";
    public const string EnableStreamingOptimization = "LLM:EnableStreamingOptimization";
    public const string EnableEmbeddingCaching = "LLM:EnableEmbeddingCaching";
    public const string EnableNewHealthCheck = "LLM:EnableNewHealthCheck";
}
```

#### **第 2-3 周：核心服务实现**

##### 步骤 2.1: 实现 Microsoft LLM 服务工厂
```csharp
// 创建 TelegramSearchBot/Service/AI/LLM/MicrosoftLLMFactory.cs
[Injectable(ServiceLifetime.Singleton)]
public class MicrosoftLLMFactory : IMicrosoftLLMFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MicrosoftLLMFactory> _logger;
    private readonly Dictionary<LLMProvider, IMicrosoftLLMService> _services;

    public MicrosoftLLMFactory(IServiceProvider serviceProvider, ILogger<MicrosoftLLMFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _services = new Dictionary<LLMProvider, IMicrosoftLLMService>();
    }

    public IMicrosoftLLMService CreateLLMService(LLMProvider provider)
    {
        if (!_services.ContainsKey(provider))
        {
            var service = provider switch
            {
                LLMProvider.OpenAI => _serviceProvider.GetRequiredService<MicrosoftOpenAIService>(),
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<MicrosoftOllamaService>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<MicrosoftGeminiService>(),
                _ => throw new NotSupportedException($"Provider {provider} is not supported")
            };
            _services[provider] = service;
        }
        return _services[provider];
    }
}
```

##### 步骤 2.2: 实现 OpenAI 适配器
```csharp
// 创建 TelegramSearchBot/Service/AI/LLM/MicrosoftOpenAIService.cs
[Injectable(ServiceLifetime.Transient)]
public class MicrosoftOpenAIService : IMicrosoftLLMService
{
    private readonly DataDbContext _dbContext;
    private readonly ILogger<MicrosoftOpenAIService> _logger;
    private readonly Dictionary<string, IChatClient> _clientCache = new();

    public MicrosoftOpenAIService(DataDbContext dbContext, ILogger<MicrosoftOpenAIService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IChatClient GetChatClient(LLMChannel channel)
    {
        var cacheKey = $"{channel.Id}:{channel.Endpoint}";
        
        if (!_clientCache.ContainsKey(cacheKey))
        {
            var openAIClient = new OpenAIClient(channel.ApiKey);
            var chatClient = openAIClient.AsChatClient(channel.ModelName);
            
            _clientCache[cacheKey] = chatClient
                .UseFunctionInvocation()
                .UseOpenTelemetry()
                .UseLogging();
        }
        
        return _clientCache[cacheKey];
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LLMChannel channel)
    {
        var openAIClient = new OpenAIClient(channel.ApiKey);
        return openAIClient.AsEmbeddingGenerator(channel.ModelName);
    }

    public async Task<bool> IsHealthyAsync(LLMChannel channel)
    {
        try
        {
            var client = GetChatClient(channel);
            var models = await client.GetChatModelsAsync();
            return models.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI service health check failed for channel {ChannelId}", channel.Id);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
    {
        try
        {
            var client = GetChatClient(channel);
            var models = await client.GetChatModelsAsync();
            return models.Select(m => m.ModelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OpenAI models for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
    {
        try
        {
            var client = GetChatClient(channel);
            var models = await client.GetChatModelsAsync();
            return models.Select(m => new ModelWithCapabilities
            {
                ModelName = m.ModelId,
                Capabilities = new List<string> { "text", "chat", "streaming" }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OpenAI models with capabilities for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<ModelWithCapabilities>();
        }
    }

    public void Dispose()
    {
        foreach (var client in _clientCache.Values)
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _clientCache.Clear();
    }
}
```

##### 步骤 2.3: 实现 Ollama 适配器
```csharp
// 创建 TelegramSearchBot/Service/AI/LLM/MicrosoftOllamaService.cs
[Injectable(ServiceLifetime.Transient)]
public class MicrosoftOllamaService : IMicrosoftLLMService
{
    private readonly DataDbContext _dbContext;
    private readonly ILogger<MicrosoftOllamaService> _logger;
    private readonly Dictionary<string, IChatClient> _clientCache = new();

    public MicrosoftOllamaService(DataDbContext dbContext, ILogger<MicrosoftOllamaService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IChatClient GetChatClient(LLMChannel channel)
    {
        var cacheKey = $"{channel.Id}:{channel.Endpoint}";
        
        if (!_clientCache.ContainsKey(cacheKey))
        {
            var ollamaClient = new OllamaClient(new Uri(channel.Endpoint));
            var chatClient = ollamaClient.AsChatClient(channel.ModelName);
            
            _clientCache[cacheKey] = chatClient
                .UseFunctionInvocation()
                .UseOpenTelemetry()
                .UseLogging();
        }
        
        return _clientCache[cacheKey];
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LLMChannel channel)
    {
        var ollamaClient = new OllamaClient(new Uri(channel.Endpoint));
        return ollamaClient.AsEmbeddingGenerator(channel.ModelName);
    }

    public async Task<bool> IsHealthyAsync(LLMChannel channel)
    {
        try
        {
            var client = GetChatClient(channel);
            var response = await client.CompleteAsync("Hello");
            return !string.IsNullOrEmpty(response.Message.Text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama service health check failed for channel {ChannelId}", channel.Id);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
    {
        try
        {
            var ollamaClient = new OllamaClient(new Uri(channel.Endpoint));
            var models = await ollamaClient.GetModelsAsync();
            return models.Select(m => m.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Ollama models for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
    {
        try
        {
            var ollamaClient = new OllamaClient(new Uri(channel.Endpoint));
            var models = await ollamaClient.GetModelsAsync();
            return models.Select(m => new ModelWithCapabilities
            {
                ModelName = m.Name,
                Capabilities = new List<string> { "text", "chat", "streaming", "embedding" }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Ollama models with capabilities for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<ModelWithCapabilities>();
        }
    }

    public void Dispose()
    {
        foreach (var client in _clientCache.Values)
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _clientCache.Clear();
    }
}
```

##### 步骤 2.4: 实现 Gemini 适配器
```csharp
// 创建 TelegramSearchBot/Service/AI/LLM/MicrosoftGeminiService.cs
[Injectable(ServiceLifetime.Transient)]
public class MicrosoftGeminiService : IMicrosoftLLMService
{
    private readonly DataDbContext _dbContext;
    private readonly ILogger<MicrosoftGeminiService> _logger;
    private readonly Dictionary<string, IChatClient> _clientCache = new();

    public MicrosoftGeminiService(DataDbContext dbContext, ILogger<MicrosoftGeminiService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IChatClient GetChatClient(LLMChannel channel)
    {
        var cacheKey = $"{channel.Id}:{channel.Endpoint}";
        
        if (!_clientCache.ContainsKey(cacheKey))
        {
            var geminiClient = new GeminiClient(channel.ApiKey);
            var chatClient = geminiClient.AsChatClient(channel.ModelName);
            
            _clientCache[cacheKey] = chatClient
                .UseFunctionInvocation()
                .UseOpenTelemetry()
                .UseLogging();
        }
        
        return _clientCache[cacheKey];
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LLMChannel channel)
    {
        var geminiClient = new GeminiClient(channel.ApiKey);
        return geminiClient.AsEmbeddingGenerator(channel.ModelName);
    }

    public async Task<bool> IsHealthyAsync(LLMChannel channel)
    {
        try
        {
            var client = GetChatClient(channel);
            var response = await client.CompleteAsync("Hello");
            return !string.IsNullOrEmpty(response.Message.Text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini service health check failed for channel {ChannelId}", channel.Id);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
    {
        try
        {
            var geminiClient = new GeminiClient(channel.ApiKey);
            var models = await geminiClient.GetModelsAsync();
            return models.Select(m => m.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Gemini models for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
    {
        try
        {
            var geminiClient = new GeminiClient(channel.ApiKey);
            var models = await geminiClient.GetModelsAsync();
            return models.Select(m => new ModelWithCapabilities
            {
                ModelName = m.Name,
                Capabilities = new List<string> { "text", "chat", "streaming", "image", "embedding" }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Gemini models with capabilities for channel {ChannelId}", channel.Id);
            return Enumerable.Empty<ModelWithCapabilities>();
        }
    }

    public void Dispose()
    {
        foreach (var client in _clientCache.Values)
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _clientCache.Clear();
    }
}
```

#### **第 4-5 周：统一适配器实现**

##### 步骤 3.1: 创建统一适配器服务
```csharp
// 创建 TelegramSearchBot/Service/AI/LLM/UnifiedLLMAdapter.cs
[Injectable(ServiceLifetime.Scoped)]
public class UnifiedLLMAdapter : ILLMService
{
    private readonly IMicrosoftLLMFactory _microsoftLLMFactory;
    private readonly ILLMFactory _legacyLLMFactory;
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<UnifiedLLMAdapter> _logger;

    public UnifiedLLMAdapter(
        IMicrosoftLLMFactory microsoftLLMFactory,
        ILLMFactory legacyLLMFactory,
        IAppConfigurationService configService,
        ILogger<UnifiedLLMAdapter> logger)
    {
        _microsoftLLMFactory = microsoftLLMFactory;
        _legacyLLMFactory = legacyLLMFactory;
        _configService = configService;
        _logger = logger;
    }

    private async Task<bool> UseNewImplementationAsync()
    {
        return await _configService.GetBoolAsync(LLMConfigurationKeys.UseMicrosoftExtensionsAI, false);
    }

    public async IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            await foreach (var result in ExecWithNewImplementationAsync(message, ChatId, modelName, channel, cancellationToken))
            {
                yield return result;
            }
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            await foreach (var result in legacyService.ExecAsync(message, ChatId, modelName, channel, cancellationToken))
            {
                yield return result;
            }
        }
    }

    private async IAsyncEnumerable<string> ExecWithNewImplementationAsync(Message message, long ChatId, string modelName, LLMChannel channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
        var chatClient = microsoftService.GetChatClient(channel);

        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant in a Telegram group chat."),
            new UserChatMessage(message.Content)
        };

        await foreach (var update in chatClient.CompleteStreamingAsync(chatMessages, null, cancellationToken))
        {
            yield return update.ContentUpdate;
        }
    }

    public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
            var embeddingGenerator = microsoftService.GetEmbeddingGenerator(channel);
            var embeddings = await embeddingGenerator.GenerateEmbeddingsAsync(new[] { text });
            return embeddings[0].Vector.ToArray();
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            return await legacyService.GenerateEmbeddingsAsync(text, modelName, channel);
        }
    }

    public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
            return await microsoftService.GetAllModels(channel);
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            return await legacyService.GetAllModels(channel);
        }
    }

    public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
            return await microsoftService.GetAllModelsWithCapabilities(channel);
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            return await legacyService.GetAllModelsWithCapabilities(channel);
        }
    }

    public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
            var chatClient = microsoftService.GetChatClient(channel);

            using var stream = File.OpenRead(photoPath);
            var imageContent = new ImageContent(stream, "image/jpeg");

            var response = await chatClient.CompleteAsync([
                new UserChatMessage("请分析这张图片的内容，提供详细的描述"),
                new UserChatMessage(imageContent)
            ]);

            return response.Message.Text;
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            return await legacyService.AnalyzeImageAsync(photoPath, modelName, channel);
        }
    }

    public async Task<bool> IsHealthyAsync(LLMChannel channel)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        if (useNewImplementation)
        {
            var microsoftService = _microsoftLLMFactory.CreateLLMService(channel.Provider);
            return await microsoftService.IsHealthyAsync(channel);
        }
        else
        {
            var legacyService = _legacyLLMFactory.GetLLMService(channel.Provider);
            return await legacyService.IsHealthyAsync(channel);
        }
    }
}
```

##### 步骤 3.2: 更新依赖注入配置
```csharp
// 在 TelegramSearchBot/Extension/ServiceCollectionExtension.cs 中添加
public static IServiceCollection AddMicrosoftLLMServices(this IServiceCollection services)
{
    // 注册 Microsoft.Extensions.AI 服务
    services.AddSingleton<IMicrosoftLLMFactory, MicrosoftLLMFactory>();
    services.AddTransient<MicrosoftOpenAIService>();
    services.AddTransient<MicrosoftOllamaService>();
    services.AddTransient<MicrosoftGeminiService>();
    
    // 注册统一适配器
    services.AddScoped<ILLMService, UnifiedLLMAdapter>();
    
    // 添加 ChatClient 全局配置
    services.AddChatClient(builder => builder
        .UseFunctionInvocation()
        .UseOpenTelemetry()
        .UseLogging());
    
    return services;
}
```

#### **第 6-7 周：GeneralLLMService 适配**

##### 步骤 4.1: 更新 GeneralLLMService 以支持新实现
```csharp
// 修改 TelegramSearchBot/Service/AI/LLM/GeneralLLMService.cs
[Injectable(ServiceLifetime.Scoped)]
public class GeneralLLMService : IService, IGeneralLLMService
{
    // ... 保持现有字段
    
    // 添加新的服务引用
    private readonly IMicrosoftLLMFactory _microsoftLLMFactory;
    private readonly IAppConfigurationService _configService;

    public GeneralLLMService(
        // ... 保持现有参数
        IMicrosoftLLMFactory microsoftLLMFactory,
        IAppConfigurationService configService)
    {
        // ... 保持现有初始化
        _microsoftLLMFactory = microsoftLLMFactory;
        _configService = configService;
    }

    private async Task<bool> UseNewImplementationAsync()
    {
        return await _configService.GetBoolAsync(LLMConfigurationKeys.UseMicrosoftExtensionsAI, false);
    }

    // 修改 ExecOperationAsync 方法以支持新实现
    public async IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(
        Func<ILLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation,
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var useNewImplementation = await UseNewImplementationAsync();
        
        // ... 保持现有的 Redis 信号量逻辑
        
        foreach (var channel in llmChannels)
        {
            var redisKey = $"llm:channel:{channel.Id}:semaphore";
            var currentCount = await redisDb.StringGetAsync(redisKey);
            int count = currentCount.HasValue ? (int)currentCount : 0;

            if (count < channel.Parallel)
            {
                await redisDb.StringIncrementAsync(redisKey);
                try
                {
                    ILLMService service;
                    
                    if (useNewImplementation)
                    {
                        // 使用新的统一适配器
                        service = _microsoftLLMFactory.CreateLLMService(channel.Provider) as ILLMService;
                    }
                    else
                    {
                        // 使用现有的服务
                        service = _LLMFactory.GetLLMService(channel.Provider);
                    }

                    bool isHealthy = false;
                    try
                    {
                        isHealthy = await service.IsHealthyAsync(channel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"LLM渠道 {channel.Id} ({channel.Provider}) 健康检查失败");
                        continue;
                    }

                    if (!isHealthy)
                    {
                        _logger.LogWarning($"LLM渠道 {channel.Id} ({channel.Provider}) 不可用，跳过");
                        continue;
                    }

                    await foreach (var e in operation(service, channel, cancellationToken))
                    {
                        yield return e;
                    }
                    yield break;
                }
                finally
                {
                    await redisDb.StringDecrementAsync(redisKey);
                }
            }
        }
        
        // ... 保持现有的重试逻辑
    }
}
```

#### **第 8-9 周：测试和验证**

##### 步骤 5.1: 创建单元测试
```csharp
// 创建 TelegramSearchBot.Test/Service/AI/LLM/MicrosoftLLMServiceTests.cs
public class MicrosoftLLMServiceTests
{
    [Fact]
    public async Task MicrosoftOpenAIService_ShouldReturnChatClient()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb")
            .Options;
        
        var dbContext = new DataDbContext(options);
        var logger = new Mock<ILogger<MicrosoftOpenAIService>>().Object;
        
        var service = new MicrosoftOpenAIService(dbContext, logger);
        
        var channel = new LLMChannel
        {
            Id = 1,
            Provider = LLMProvider.OpenAI,
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "test-key",
            ModelName = "gpt-3.5-turbo"
        };
        
        // Act
        var chatClient = service.GetChatClient(channel);
        
        // Assert
        Assert.NotNull(chatClient);
    }

    [Fact]
    public async Task UnifiedLLMAdapter_ShouldUseNewImplementation_WhenConfigured()
    {
        // Arrange
        var configService = new Mock<IAppConfigurationService>();
        configService.Setup(x => x.GetBoolAsync(LLMConfigurationKeys.UseMicrosoftExtensionsAI, false))
            .ReturnsAsync(true);
        
        var microsoftFactory = new Mock<IMicrosoftLLMFactory>();
        var legacyFactory = new Mock<ILLMFactory>();
        var logger = new Mock<ILogger<UnifiedLLMAdapter>>().Object;
        
        var adapter = new UnifiedLLMAdapter(
            microsoftFactory.Object,
            legacyFactory.Object,
            configService.Object,
            logger);
        
        var channel = new LLMChannel { Provider = LLMProvider.OpenAI };
        
        // Act
        var models = await adapter.GetAllModels(channel);
        
        // Assert
        microsoftFactory.Verify(x => x.CreateLLMService(LLMProvider.OpenAI), Times.Once);
        legacyFactory.Verify(x => x.GetLLMService(LLMProvider.OpenAI), Times.Never);
    }
}
```

##### 步骤 5.2: 创建集成测试
```csharp
// 创建 TelegramSearchBot.Test/Service/AI/LLM/LLMMigrationIntegrationTests.cs
public class LLMMigrationIntegrationTests
{
    [Fact]
    public async Task Migration_ShouldNotBreakExistingFunctionality()
    {
        // 这个测试验证新旧实现的兼容性
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<DataDbContext>(options => 
            options.UseInMemoryDatabase("MigrationTestDb"));
        services.AddLogging();
        services.AddMicrosoftLLMServices();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Act & Assert
        var adapter = serviceProvider.GetRequiredService<ILLMService>();
        
        // 测试所有接口方法都能正常工作
        Assert.NotNull(adapter);
        
        var channel = new LLMChannel
        {
            Id = 1,
            Provider = LLMProvider.OpenAI,
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "test-key",
            ModelName = "gpt-3.5-turbo"
        };
        
        // 测试健康检查
        var isHealthy = await adapter.IsHealthyAsync(channel);
        Assert.True(isHealthy);
        
        // 测试获取模型列表
        var models = await adapter.GetAllModels(channel);
        Assert.NotNull(models);
    }
}
```

#### **第 10-12 周：部署和监控**

##### 步骤 6.1: 创建部署脚本
```bash
#!/bin/bash
# 创建 deploy-migration.sh

echo "开始部署 Microsoft.Extensions.AI 迁移..."

# 1. 备份当前数据库
echo "备份数据库..."
dotnet ef database update --context DataDbContext --connection "$BackupConnectionString"

# 2. 创建迁移
echo "创建数据库迁移..."
dotnet ef migrations add AddMicrosoftLLMConfiguration --context DataDbContext

# 3. 应用迁移
echo "应用数据库迁移..."
dotnet ef database update --context DataDbContext

# 4. 构建项目
echo "构建项目..."
dotnet build --configuration Release

# 5. 部署到测试环境
echo "部署到测试环境..."
dotnet publish --configuration Release --output ./publish

echo "部署完成！"
```

##### 步骤 6.2: 创建监控和回滚脚本
```bash
#!/bin/bash
# 创建 rollback-migration.sh

echo "开始回滚 Microsoft.Extensions.AI 迁移..."

# 1. 更新配置以禁用新实现
echo "更新配置..."
# 这里需要根据实际配置存储方式来实现

# 2. 重启应用
echo "重启应用..."
systemctl restart telegram-search-bot

# 3. 验证服务状态
echo "验证服务状态..."
systemctl status telegram-search-bot

echo "回滚完成！"
```

### 9.3 测试策略

#### 单元测试
- 每个新的服务类都要有完整的单元测试
- 测试新旧实现的切换逻辑
- 测试配置读取和特性开关

#### 集成测试
- 测试完整的调用链路
- 测试数据库集成
- 测试 Redis 并发控制

#### 性能测试
- 对比新旧实现的性能差异
- 测试并发处理能力
- 测试内存使用情况

#### 用户验收测试
- 在测试环境中运行新实现
- 邀请核心用户测试
- 收集反馈并优化

### 9.4 部署策略

#### 阶段 1: 测试环境 (第 10 周)
1. 在测试环境部署新实现
2. 使用特性开关禁用新功能
3. 验证系统稳定性

#### 阶段 2: 小规模试用 (第 11 周)
1. 为特定群组启用新实现
2. 监控性能和错误率
3. 收集用户反馈

#### 阶段 3: 逐步推广 (第 12 周)
1. 根据测试结果逐步扩大使用范围
2. 持续监控和优化
3. 准备回滚方案

### 9.5 监控指标

#### 关键指标
- **响应时间**: 对比新旧实现的响应时间
- **错误率**: 监控 API 调用错误率
- **并发能力**: 监控并发处理能力
- **内存使用**: 监控内存使用情况
- **CPU 使用率**: 监控 CPU 使用率

#### 告警规则
- 响应时间超过阈值
- 错误率超过 1%
- 内存使用超过 80%
- CPU 使用超过 70%

### 9.6 回滚计划

#### 回滚触发条件
1. 错误率超过 5%
2. 响应时间增加超过 50%
3. 内存泄漏或 CPU 异常
4. 用户反馈严重问题

#### 回滚步骤
1. 更新配置禁用新实现
2. 重启应用服务
3. 验证服务恢复
4. 分析问题原因
5. 修复后重新部署

---

**注意**: 具体的实现细节可能需要根据 Microsoft.Extensions.AI 的正式版本进行调整。建议在实施前先创建概念验证 (POC) 来验证核心功能的可行性。