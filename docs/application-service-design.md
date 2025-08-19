# TelegramSearchBot 应用服务层设计

## 架构概述

应用服务层位于表现层（Controller）和领域层之间，负责：
- 协调领域对象和基础设施
- 实现业务用例
- 处理事务边界
- 执行DTO映射

## 项目结构

```
TelegramSearchBot.Application/
├── DTOs/                    # 数据传输对象
│   ├── Requests/           # 请求DTO
│   ├── Responses/          # 响应DTO
│   └── Mappings/           # 映射配置
├── Features/               # 功能模块
│   ├── Messages/           # 消息相关功能
│   ├── Search/             # 搜索相关功能
│   └── AI/                 # AI相关功能
├── Abstractions/          # 应用服务抽象
│   ├── IApplicationService.cs
│   ├── IRequestHandler.cs
│   └── INotificationHandler.cs
├── Behaviors/              # 管道行为
│   ├── ValidationBehavior.cs
│   ├── LoggingBehavior.cs
│   └── TransactionBehavior.cs
├── Exceptions/             # 应用层异常
│   └── ApplicationException.cs
└── TelegramSearchBot.Application.csproj
```

## 核心应用服务

### 1. MessageApplicationService
```csharp
// 命令
public record CreateMessageCommand(MessageDto MessageDto) : IRequest<long>;
public record UpdateMessageCommand(long Id, MessageDto MessageDto) : IRequest;
public record DeleteMessageCommand(long Id) : IRequest;

// 查询
public record GetMessageByIdQuery(long Id) : IRequest<MessageDto>;
public record GetMessagesByGroupQuery(long GroupId, int Skip, int Take) : IRequest<IEnumerable<MessageDto>>;
public record SearchMessagesQuery(SearchRequest Request) : IRequest<SearchResponseDto>;

// 服务接口
public interface IMessageApplicationService : IApplicationService
{
    Task<long> CreateMessageAsync(CreateMessageCommand command);
    Task UpdateMessageAsync(UpdateMessageCommand command);
    Task DeleteMessageAsync(DeleteMessageCommand command);
    Task<MessageDto> GetMessageByIdAsync(GetMessageByIdQuery query);
    Task<IEnumerable<MessageDto>> GetMessagesByGroupAsync(GetMessagesByGroupQuery query);
    Task<SearchResponseDto> SearchMessagesAsync(SearchMessagesQuery query);
}
```

### 2. SearchApplicationService
```csharp
// 命令
public record PerformSearchCommand(SearchRequest Request) : IRequest<SearchResponseDto>;
public record IndexDocumentCommand(long MessageId) : IRequest;

// 查询
public record GetSearchHistoryQuery(long UserId, int Limit) : IRequest<IEnumerable<SearchHistoryDto>>;
public record GetSearchSuggestionsQuery(string Query, int Limit) : IRequest<IEnumerable<string>>;

// 服务接口
public interface ISearchApplicationService : IApplicationService
{
    Task<SearchResponseDto> PerformSearchAsync(PerformSearchCommand command);
    Task IndexDocumentAsync(IndexDocumentCommand command);
    Task<IEnumerable<SearchHistoryDto>> GetSearchHistoryAsync(GetSearchHistoryQuery query);
    Task<IEnumerable<string>> GetSearchSuggestionsAsync(GetSearchSuggestionsQuery query);
}
```

## DTO设计

### Message相关DTO
```csharp
// 请求DTO
public class MessageDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public long MessageId { get; set; }
    public long FromUserId { get; set; }
    public string Content { get; set; }
    public DateTime DateTime { get; set; }
    public IEnumerable<MessageExtensionDto> Extensions { get; set; }
}

// 响应DTO
public class MessageResponseDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public long MessageId { get; set; }
    public string Content { get; set; }
    public DateTime DateTime { get; set; }
    public UserInfoDto FromUser { get; set; }
    public IEnumerable<MessageExtensionDto> Extensions { get; set; }
}
```

### 搜索相关DTO
```csharp
public class SearchRequest
{
    public string Query { get; set; }
    public long? GroupId { get; set; }
    public SearchType SearchType { get; set; } = SearchType.InvertedIndex;
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 20;
}

public class SearchResponseDto
{
    public IEnumerable<MessageResponseDto> Messages { get; set; }
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public SearchType SearchType { get; set; }
}
```

## CQRS实现

### Command Bus
```csharp
public interface ICommandBus
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
    Task Send(IRequest request);
    Task Publish(INotification notification);
}

public class CommandBus : ICommandBus
{
    private readonly IMediator _mediator;
    
    public CommandBus(IMediator mediator)
    {
        _mediator = mediator;
    }
    
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
        => _mediator.Send(request);
    
    public Task Send(IRequest request)
        => _mediator.Send(request);
    
    public Task Publish(INotification notification)
        => _mediator.Publish(notification);
}
```

### Query Bus
```csharp
public interface IQueryBus
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
}

public class QueryBus : IQueryBus
{
    private readonly IMediator _mediator;
    
    public QueryBus(IMediator mediator)
    {
        _mediator = mediator;
    }
    
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
        => _mediator.Send(request);
}
```

## 异常处理

### ApplicationException
```csharp
public class ApplicationException : Exception
{
    public string ErrorCode { get; }
    public object Details { get; }
    
    public ApplicationException(string message, string errorCode = null, object details = null)
        : base(message)
    {
        ErrorCode = errorCode;
        Details = details;
    }
}

// 具体异常类型
public class MessageNotFoundException : ApplicationException
{
    public MessageNotFoundException(long messageId) 
        : base($"Message with ID {messageId} not found", "MESSAGE_NOT_FOUND", new { MessageId = messageId })
    {
    }
}

public class SearchException : ApplicationException
{
    public SearchException(string query, Exception innerException = null) 
        : base($"Search failed for query: {query}", "SEARCH_FAILED", innerException)
    {
    }
}
```

## 依赖注入配置

```csharp
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 注册MediatR
        services.AddMediatR(cfg => 
        {
            cfg.RegisterServicesFromAssemblyContaining<ApplicationAssemblyReference>();
        });
        
        // 注册应用服务
        services.AddScoped<IMessageApplicationService, MessageApplicationService>();
        services.AddScoped<ISearchApplicationService, SearchApplicationService>();
        
        // 注册CQRS总线
        services.AddScoped<ICommandBus, CommandBus>();
        services.AddScoped<IQueryBus, QueryBus>();
        
        // 注册AutoMapper
        services.AddAutoMapper(typeof(ApplicationAssemblyReference));
        
        // 注册管道行为
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        
        return services;
    }
}
```

这个设计遵循了Clean Architecture原则，实现了CQRS模式，并提供了清晰的分层结构。接下来我将开始具体的TDD实现。