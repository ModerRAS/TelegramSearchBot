# TelegramSearchBot.LLM

TelegramSearchBot的LLM（大语言模型）模块，基于DDD（领域驱动设计）架构设计，提供统一的LLM服务抽象层。

## 架构设计

本项目采用DDD架构，包含以下层次：

### Domain（领域层）
- **Entities**: 核心业务实体
  - `LLMProviderEntity`: LLM提供商聚合根
- **ValueObjects**: 值对象
  - `LLMRequest`: LLM请求
  - `LLMResponse`: LLM响应
  - `LLMChannelConfig`: 渠道配置
  - `ToolDefinition`: 工具定义
  - `ToolInvocation`: 工具调用
- **Services**: 领域服务接口
  - `ILLMService`: LLM服务接口
  - `IToolInvocationService`: 工具调用服务接口
- **Factories**: 工厂接口
  - `ILLMServiceFactory`: LLM服务抽象工厂
  - `ILLMServiceFactoryManager`: 工厂管理器

### Application（应用层）
- **Services**: 应用服务
  - `LLMApplicationService`: LLM应用服务，协调业务逻辑

### Infrastructure（基础设施层）
- **Services**: 具体实现
  - `OpenAILLMService`: OpenAI服务实现
  - `DefaultToolInvocationService`: 工具调用服务实现
- **Decorators**: 装饰器实现
  - `LoggingLLMServiceDecorator`: 日志装饰器
  - `ToolInvocationLLMServiceDecorator`: 工具调用装饰器
- **Factories**: 工厂实现
  - `OpenAIServiceFactory`: OpenAI服务工厂
  - `DecoratedServiceFactory`: 装饰器工厂
  - `LLMServiceFactoryManager`: 工厂管理器实现
- **Tools**: 内置工具
  - `BuiltInTools`: 内置工具集合
- **Extensions**: 扩展方法
  - `ServiceCollectionExtensions`: 依赖注入配置

## 主要特性

- ✅ **装饰器模式**: 支持日志记录、工具调用等功能的动态组合
- ✅ **抽象工厂模式**: 支持多种LLM提供商的统一接入
- ✅ **DDD架构**: 清晰的分层和关注点分离
- ✅ **工具调用**: 支持LLM调用外部工具和函数
- ✅ **流式响应**: 支持实时流式数据返回
- ✅ **多模态支持**: 支持文本、图片等多种内容类型
- ✅ **异步设计**: 全异步API设计，提高性能
- ✅ **依赖注入**: 完整的DI支持
- ✅ **BDD测试**: 行为驱动开发的单元测试

## 支持的LLM提供商

- [x] OpenAI (GPT系列)
- [ ] Ollama (本地模型)
- [ ] Google Gemini
- [ ] Claude (Anthropic)

## 支持的装饰器

### 日志装饰器 (`LoggingLLMServiceDecorator`)
- 记录请求开始和结束时间
- 记录执行时长和性能指标
- 记录错误和异常信息
- 支持结构化日志记录

### 工具调用装饰器 (`ToolInvocationLLMServiceDecorator`)
- 自动识别LLM输出中的工具调用请求
- 执行工具调用并返回结果给LLM
- 支持多轮工具调用
- 防止无限递归调用

## 内置工具

### 时间工具
- `get_current_time`: 获取当前日期和时间
- `format_time`: 格式化时间字符串

### 计算工具
- `calculator`: 基础数学计算
- `math_function`: 数学函数计算（sin, cos, log等）

### 文本工具
- `text_stats`: 文本统计分析
- `base64_encode_decode`: Base64编码/解码

## 快速开始

### 1. 安装依赖

```xml
<PackageReference Include="TelegramSearchBot.LLM" Version="1.0.0" />
```

### 2. 配置服务

```csharp
// Program.cs 或 Startup.cs
services.AddTelegramSearchBotLLM()
    .ConfigureLLMProviders(providers =>
    {
        providers.AddOpenAI(decorators =>
        {
            decorators.EnableLogging = true;           // 启用日志装饰器
            decorators.EnableToolInvocation = true;    // 启用工具调用装饰器
            decorators.MaxToolInvocations = 5;         // 最大工具调用次数
        });
    });
```

### 3. 注册自定义工具

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTelegramSearchBotLLM()
            .ConfigureLLMProviders(providers =>
            {
                providers.AddOpenAI(decorators =>
                {
                    decorators.EnableToolInvocation = true;
                });
            });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // 注册内置工具
        var toolService = app.ApplicationServices.GetRequiredService<IToolInvocationService>();
        BuiltInTools.RegisterAll(toolService);

        // 注册自定义工具
        var weatherToolDefinition = new ToolDefinition(
            Name: "get_weather",
            Description: "获取指定城市的天气信息",
            Parameters: new List<ToolParameter>
            {
                new("city", ToolParameterType.String, "城市名称", true)
            });

        toolService.RegisterTool(weatherToolDefinition, async parameters =>
        {
            var city = parameters["city"].ToString();
            // 实际实现中这里会调用天气API
            return new { city = city, temperature = "22°C", condition = "晴天" };
        });
    }
}
```

### 4. 使用服务

```csharp
public class ChatController
{
    private readonly LLMApplicationService _llmService;

    public ChatController(LLMApplicationService llmService)
    {
        _llmService = llmService;
    }

    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var llmRequest = new LLMRequest(
            RequestId: Guid.NewGuid().ToString(),
            Model: "gpt-3.5-turbo",
            Channel: new LLMChannelConfig(
                Gateway: "https://api.openai.com/v1",
                ApiKey: "your-api-key"
            ),
            ChatHistory: new List<LLMMessage>
            {
                new(LLMRole.User, request.Message)
            }
        );

        var response = await _llmService.ExecuteAsync(
            LLMProvider.OpenAI, 
            llmRequest
        );

        return Ok(response);
    }
}
```

### 5. 流式响应

```csharp
public async Task<IActionResult> StreamChat([FromBody] ChatRequest request)
{
    var llmRequest = new LLMRequest(/* ... */);
    
    var (streamReader, responseTask) = await _llmService.ExecuteStreamAsync(
        LLMProvider.OpenAI, 
        llmRequest
    );

    Response.ContentType = "text/plain";
    
    await foreach (var chunk in streamReader.ReadAllAsync())
    {
        await Response.WriteAsync(chunk);
        await Response.Body.FlushAsync();
    }

    var finalResponse = await responseTask;
    return Ok(finalResponse);
}
```

### 6. 工具调用示例

当用户问"现在几点了？"，LLM会自动调用工具：

```
用户: 现在几点了？

LLM: 我来帮你查看当前时间。
<tool_call>
{
    "tool_name": "get_current_time",
    "parameters": {
        "timezone": "Local"
    }
}
</tool_call>

系统: [自动执行工具调用]
工具调用结果:
工具 get_current_time 执行成功:
{"current_time":"2024-01-01 12:00:00","timezone":"Local","day_of_week":"Monday"}

LLM: 当前时间是2024年1月1日 12:00:00，今天是星期一。
```

## 测试

项目使用BDD（行为驱动开发）风格的单元测试：

```bash
dotnet test TelegramSearchBot.LLM.Tests
```

### 测试示例

```csharp
public class 当使用装饰器模式时 : BddTestBase
{
    protected override Task Given()
    {
        // Given: 我有一个配置了装饰器的LLM服务
    }

    protected override Task When()
    {
        // When: 我执行LLM请求
    }

    protected override Task Then()
    {
        // Then: 应该正确应用所有装饰器
    }

    [Fact]
    public async Task 应该正确组合装饰器功能()
    {
        await RunTest();
    }
}
```

## 装饰器模式设计

装饰器按以下顺序应用：
```
核心LLM服务 → 工具调用装饰器 → 日志装饰器
```

这样的顺序确保：
1. 工具调用在核心功能之上运行
2. 日志记录包含工具调用的详细信息
3. 每个装饰器都可以独立启用/禁用

## 扩展新的装饰器

1. **实现装饰器基类**:
```csharp
public class CustomLLMServiceDecorator : LLMServiceDecoratorBase
{
    public CustomLLMServiceDecorator(ILLMService innerService) 
        : base(innerService) { }

    public override async Task<LLMResponse> ExecuteAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        // 自定义前置处理
        var response = await base.ExecuteAsync(request, cancellationToken);
        // 自定义后置处理
        return response;
    }
}
```

2. **在工厂中注册**:
```csharp
public class CustomDecoratedServiceFactory : ILLMServiceFactory
{
    public ILLMService CreateService()
    {
        var service = _baseFactory.CreateService();
        return new CustomLLMServiceDecorator(service);
    }
}
```

## 扩展新的LLM提供商

1. **实现领域服务接口**:
```csharp
public class CustomLLMService : ILLMService
{
    // 实现接口方法
}
```

2. **创建具体工厂**:
```csharp
public class CustomServiceFactory : ILLMServiceFactory
{
    public LLMProvider SupportedProvider => LLMProvider.Custom;
    
    public ILLMService CreateService()
    {
        return new CustomLLMService();
    }
}
```

3. **注册服务**:
```csharp
services.ConfigureLLMProviders(providers =>
{
    providers.AddCustom(decorators =>
    {
        decorators.EnableLogging = true;
        decorators.EnableToolInvocation = true;
    });
});
```

## 扩展新的工具

```csharp
var customToolDefinition = new ToolDefinition(
    Name: "custom_tool",
    Description: "自定义工具描述",
    Parameters: new List<ToolParameter>
    {
        new("param1", ToolParameterType.String, "参数1描述", true),
        new("param2", ToolParameterType.Number, "参数2描述", false)
    },
    Category: "自定义工具");

toolService.RegisterTool(customToolDefinition, async parameters =>
{
    // 工具实现逻辑
    var param1 = parameters["param1"].ToString();
    var param2 = parameters.TryGetValue("param2", out var p2) ? Convert.ToDouble(p2) : 0;
    
    // 返回工具执行结果
    return new { result = "执行成功", data = "..." };
});
```

## 贡献

欢迎提交Issue和Pull Request！

## 许可证

MIT License 