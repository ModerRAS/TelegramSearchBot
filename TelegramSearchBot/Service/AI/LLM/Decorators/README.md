# LLM装饰器模式架构

本模块实现了基于装饰器模式的LLM服务架构，提供了流控、渠道选择、工具调用和日志记录等功能。

## 架构概述

```
┌─────────────────────────────────────────────────────────────┐
│                    装饰器架构图                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐    ┌─────────────────┐                │
│  │ 渠道选择装饰器   │    │ 装饰器工厂      │                │
│  │ChannelSelection │<───│ DecoratorFactory│                │
│  └─────────────────┘    └─────────────────┘                │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────┐                                        │
│  │ 日志装饰器      │                                        │
│  │ LoggingDecorator│                                        │
│  └─────────────────┘                                        │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────┐                                        │
│  │ 工具调用装饰器   │                                        │
│  │ ToolInvocation  │                                        │
│  └─────────────────┘                                        │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────┐                                        │
│  │ 流控装饰器      │                                        │
│  │ RateLimitDecorator│                                      │
│  └─────────────────┘                                        │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────┐                                        │
│  │ 适配器          │                                        │
│  │ LLMServiceAdapter│                                       │
│  └─────────────────┘                                        │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────┐                                        │
│  │ 原始LLM服务     │                                        │
│  │ OllamaService   │                                        │
│  │ OpenAIService   │                                        │
│  │ GeminiService   │                                        │
│  └─────────────────┘                                        │
└─────────────────────────────────────────────────────────────┘
```

## 核心组件

### 1. 接口层
- **ILLMStreamService**: 统一的流式LLM服务接口
- **BaseLLMDecorator**: 装饰器基类

### 2. 适配器层
- **LLMServiceAdapter**: 将现有服务适配为新接口

### 3. 装饰器层
- **RateLimitDecorator**: 流控装饰器
- **ChannelSelectionDecorator**: 渠道选择装饰器
- **ToolInvocationDecorator**: 工具调用装饰器
- **LoggingDecorator**: 日志装饰器

### 4. 工厂层
- **LLMDecoratorFactory**: 装饰器工厂

### 5. 服务层
- **DecoratedGeneralLLMService**: 新的通用LLM服务

## 功能特性

### 流控装饰器 (RateLimitDecorator)
- **并发控制**: 基于Redis的分布式信号量
- **重试机制**: 可配置的重试次数和延迟
- **原子操作**: 确保并发计数的准确性
- **优雅降级**: 超时或达到限制时的处理

```csharp
// Redis键格式: llm:channel:{channelId}:semaphore
// 支持多渠道独立限流
```

### 渠道选择装饰器 (ChannelSelectionDecorator)
- **优先级排序**: 按渠道优先级选择
- **健康检查**: 自动跳过不健康的渠道
- **故障转移**: 自动切换到备用渠道
- **负载均衡**: 分布式渠道选择

### 工具调用装饰器 (ToolInvocationDecorator)
- **XML解析**: 支持McpToolHelper的工具调用格式
- **循环控制**: 防止无限工具调用循环
- **错误处理**: 工具执行失败的恢复机制
- **流式处理**: 保持实时响应体验

### 日志装饰器 (LoggingDecorator)
- **请求跟踪**: 唯一RequestId追踪
- **性能监控**: Token速度、响应时间统计
- **错误记录**: 详细的错误信息和堆栈
- **容量监控**: 渠道使用情况记录

## 使用方式

### 基本使用
```csharp
// 1. 获取装饰器工厂
var factory = serviceProvider.GetRequiredService<LLMDecoratorFactory>();

// 2. 获取原始服务并创建适配器
var originalService = serviceProvider.GetRequiredService<OllamaService>();
var adapter = new LLMServiceAdapter(originalService);

// 3. 应用装饰器
var decoratedService = factory.CreateFullDecorator(adapter, "AI Assistant");

// 4. 使用装饰过的服务
await foreach (var token in decoratedService.ExecAsync(message, chatId, modelName, channel, cancellationToken))
{
    Console.Write(token);
}
```

### 自定义配置
```csharp
var options = new LLMDecoratorOptions
{
    EnableLogging = true,
    EnableRateLimit = true,
    EnableToolInvocation = true,
    BotName = "专业AI助手",
    MaxToolCycles = 3,
    MaxRetries = 50,
    RetryDelay = TimeSpan.FromSeconds(2)
};

var decoratedService = factory.CreateDecoratedService(adapter, options);
```

### 渠道自动选择
```csharp
var channelSelector = factory.CreateChannelSelectionDecorator();

// 自动选择最佳渠道
await foreach (var token in channelSelector.ExecAsync(message, chatId, modelName, null, cancellationToken))
{
    Console.Write(token);
}
```

## 配置说明

### Redis配置
流控装饰器需要Redis支持：
```csharp
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
```

### 依赖注入配置
```csharp
// 注册核心服务
services.AddScoped<LLMDecoratorFactory>();
services.AddScoped<DecoratedGeneralLLMService>();

// 注册装饰器
services.AddScoped<RateLimitDecorator>();
services.AddScoped<LoggingDecorator>();
services.AddScoped<ToolInvocationDecorator>();
services.AddScoped<ChannelSelectionDecorator>();

// 替换原有服务
services.AddSingleton<IGeneralLLMService>(provider => 
    provider.GetRequiredService<DecoratedGeneralLLMService>());
```

## 性能优化

### 1. 连接池管理
- 使用IHttpClientFactory管理HTTP连接
- Redis连接复用

### 2. 内存优化
- 流式处理避免大对象堆积
- 及时释放资源

### 3. 并发优化
- 异步流处理
- 非阻塞的信号量实现

### 4. 缓存策略
- 渠道健康状态缓存
- 配置信息缓存

## 监控和运维

### 日志级别
- **Information**: 请求开始/完成、渠道选择
- **Debug**: Token进度、详细执行信息
- **Warning**: 重试、健康检查失败
- **Error**: 执行错误、系统异常

### 关键指标
- 请求响应时间
- Token生成速度
- 渠道可用率
- 并发使用情况
- 工具调用成功率

### 故障排查
1. 检查Redis连接状态
2. 验证渠道配置和健康状态
3. 查看并发限制设置
4. 检查工具注册状态

## 扩展指南

### 添加新装饰器
1. 继承BaseLLMDecorator
2. 实现特定功能逻辑
3. 在LLMDecoratorFactory中注册
4. 更新LLMDecoratorOptions

### 集成新的LLM服务
1. 实现原始LLM服务接口
2. 使用LLMServiceAdapter适配
3. 在LLMFactory中注册
4. 配置渠道信息

### 自定义工具
1. 使用McpToolAttribute标记方法
2. 在应用启动时调用McpToolHelper.EnsureInitialized
3. 工具会自动被ToolInvocationDecorator识别

## 注意事项

1. **Redis依赖**: 流控功能需要Redis支持
2. **工具初始化**: 需要在应用启动时初始化McpToolHelper
3. **异常处理**: 装饰器会传播异常，需要在上层处理
4. **配置同步**: 渠道配置变更需要重启或实现动态刷新
5. **资源清理**: 确保在finally块中释放信号量

## 迁移指南

从原有GeneralLLMService迁移到装饰器模式：

1. **替换服务注册**:
```csharp
// 原有
services.AddScoped<GeneralLLMService>();

// 新的
services.AddScoped<DecoratedGeneralLLMService>();
services.AddScoped<LLMDecoratorFactory>();
```

2. **更新依赖注入**:
```csharp
// 使用新的服务
services.AddSingleton<IGeneralLLMService>(provider => 
    provider.GetRequiredService<DecoratedGeneralLLMService>());
```

3. **配置验证**: 确保Redis和数据库配置正确

4. **功能测试**: 验证流控、工具调用、渠道选择功能

通过装饰器模式，我们实现了更加灵活、可扩展的LLM服务架构，同时保持了与原有接口的兼容性。 