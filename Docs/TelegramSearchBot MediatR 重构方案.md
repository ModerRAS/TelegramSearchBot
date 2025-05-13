# TelegramSearchBot MediatR中介者模式重构方案

## 1. 目标与背景

随着业务复杂度提升，原有基于Controller直连Service/Grain的架构逐渐暴露出耦合高、测试难、扩展性不足等问题。经过对Orleans Actor模型的尝试后，发现其分布式Actor适合高并发、分布式场景，但对于本项目以Bot消息驱动、业务流程为主的场景，MediatR中介者模式更能带来解耦、灵活和高可测试性的优势。因此，决定放弃Orleans方案，全面采用MediatR重构Controller层，实现业务逻辑的彻底解耦和现代化。

## 2. 新架构总览

### 2.1 架构核心思想
- **中介者模式**：所有业务消息（命令、查询、事件）都通过MediatR的IMediator接口分发，Controller层不再直接依赖Service，而是通过消息驱动Handler。
- **分层结构**：
  - **入口适配层**：如Bot消息、Web API、定时任务等，将外部输入封装为MediatR Request/Notification。
  - **MediatR Handler层**：每个业务场景对应一个Handler，专注处理单一业务逻辑。
  - **Service/存储层**：Handler通过依赖注入调用Service、数据库等。
- **消息流转**：入口适配器 -> IMediator.Send/Publish -> Handler -> Service/存储 -> Handler返回结果 -> 入口适配器响应

### 2.2 架构图

```
[Bot消息/Web API/定时任务]
        ↓
   [入口适配器]
        ↓
   [IMediator]
        ↓
   [MediatR Handler]
        ↓
   [Service/存储/第三方API]
```

## 3. 控制器重构设计

- **Controller全部迁移为MediatR Handler**：每个原Controller的业务方法，重构为一个Request/Notification+Handler。
- **入口适配**：如Bot Update、HTTP请求等，统一在入口处将消息封装为MediatR消息对象。
- **业务分发**：所有业务分发、流程编排均通过IMediator完成，Handler之间可通过Notification实现事件驱动。
- **依赖注入**：Handler通过构造函数注入Service、配置、日志等依赖，便于Mock和单元测试。
- **横切关注**：如权限、日志、事务等可通过MediatR Pipeline Behavior实现AOP式统一处理。

## 4. 典型业务流程示例

### 4.1 Bot消息处理
1. Bot收到Update，入口适配器将其封装为`HandleUpdateRequest`。
2. 通过`IMediator.Send(request)`分发到`HandleUpdateHandler`。
3. Handler内部根据消息类型再分发到具体业务Handler（如OcrRequestHandler、SearchRequestHandler等）。
4. 业务Handler处理后返回结果，由入口适配器决定如何回复用户。

### 4.2 AI/OCR/ASR等
- 每种AI任务定义独立的Request/Handler，如`OcrRequest`+`OcrRequestHandler`，`AsrRequest`+`AsrRequestHandler`。
- Handler内部只关注业务处理，依赖注入AI服务。

### 4.3 B站/搜索/管理等
- B站解析、搜索、管理命令等均定义为Request/Handler，入口适配器统一分发。
- 支持多步对话、状态机等复杂流程，可通过Notification和Pipeline Behavior实现。

## 5. 迁移步骤与建议

1. **梳理现有Controller职责**，为每个业务场景设计Request/Notification类型。
2. **实现对应Handler**，将原Controller逻辑迁移到Handler。
3. **重构入口适配器**，如Bot消息、Web API等，统一通过IMediator分发。
4. **配置依赖注入**，在Startup/GeneralBootstrap中注册MediatR及所有Handler。
5. **分阶段迁移**，可通过功能开关灰度切换，逐步替换原Controller。
6. **补充单元测试**，确保Handler的独立可测性。
7. **文档与培训**，同步更新开发文档，培训团队成员。

## 6. 代码示例

### 6.1 Request/Handler定义
```csharp
// 以搜索为例
public class SearchRequest : IRequest<SearchResult>
{
    public long ChatId { get; set; }
    public string Query { get; set; }
}

public class SearchResult
{
    public List<string> Items { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class SearchRequestHandler : IRequestHandler<SearchRequest, SearchResult>
{
    private readonly ISearchService _searchService;
    public SearchRequestHandler(ISearchService searchService)
    {
        _searchService = searchService;
    }
    public async Task<SearchResult> Handle(SearchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _searchService.SearchAsync(request.Query, request.ChatId);
            return new SearchResult { Items = items, Success = true };
        }
        catch (Exception ex)
        {
            return new SearchResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
```

### 6.2 入口适配器
```csharp
// Bot Update入口
public async Task HandleBotUpdate(Update update)
{
    var request = new HandleUpdateRequest { Update = update };
    var result = await _mediator.Send(request);
    // 根据result决定如何回复用户
}
```

### 6.3 依赖注入配置
```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GeneralBootstrap>());
// 注册所有Handler和Service
```

## 7. 总结与展望

通过MediatR中介者模式重构，TelegramSearchBot将实现：
- 业务逻辑彻底解耦，便于维护和扩展
- Handler可独立测试，开发效率提升
- 支持复杂业务流程编排和事件驱动
- 入口适配灵活，便于多渠道接入

建议优先在AI、B站、搜索等核心模块试点，逐步推广到全项目，最终实现高内聚、低耦合、易维护的现代化Bot架构。 