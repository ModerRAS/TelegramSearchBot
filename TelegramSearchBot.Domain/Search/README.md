# Search领域DDD架构使用指南

## 概述

Search领域实现了完整的领域驱动设计（DDD）架构，提供了灵活、可扩展的搜索功能。该架构包含以下核心组件：

- **聚合根（SearchAggregate）**：封装搜索会话的业务逻辑和状态管理
- **值对象**：SearchId、SearchQuery、SearchCriteria、SearchResult、SearchFilter等
- **领域服务**：SearchDomainService处理复杂的搜索业务逻辑
- **仓储接口**：ISearchRepository定义数据访问契约
- **领域事件**：SearchSessionStartedEvent、SearchCompletedEvent等
- **适配器**：与现有模型的兼容性转换

## 基本使用示例

### 1. 创建搜索会话

```csharp
using TelegramSearchBot.Domain.Search;
using TelegramSearchBot.Domain.Search.ValueObjects;

// 创建简单的搜索会话
var searchAggregate = SearchAggregate.Create("hello world", SearchTypeValue.InvertedIndex());

// 创建带过滤器的搜索会话
var filter = new SearchFilter(chatId: 12345, startDate: DateTime.Now.AddDays(-30));
var searchAggregateWithFilter = SearchAggregate.Create("hello world", SearchTypeValue.Vector(), filter);

// 创建带分页的搜索会话
var searchAggregateWithPaging = SearchAggregate.Create("hello world", SearchTypeValue.InvertedIndex(), null, 0, 50);
```

### 2. 执行搜索

```csharp
using TelegramSearchBot.Domain.Search.Services;

// 注入依赖
var searchDomainService = new SearchDomainService(searchRepository);

// 执行搜索
var result = await searchDomainService.ExecuteSearchAsync(searchAggregate);

// 检查结果
if (result.TotalResults > 0)
{
    Console.WriteLine($"找到 {result.TotalResults} 条结果");
    Console.WriteLine($"当前显示第 {result.CurrentPage} 页，共 {result.TotalPages} 页");
}
```

### 3. 分页处理

```csharp
// 下一页
if (result.HasMoreResults)
{
    searchAggregate.NextPage();
    var nextPageResult = await searchDomainService.ExecuteSearchAsync(searchAggregate);
}

// 上一页
if (searchAggregate.HasPreviousPage())
{
    searchAggregate.PreviousPage();
    var prevPageResult = await searchDomainService.ExecuteSearchAsync(searchAggregate);
}

// 跳转到指定页
searchAggregate.GoToPage(5);
var page5Result = await searchDomainService.ExecuteSearchAsync(searchAggregate);
```

### 4. 使用高级过滤器

```csharp
// 创建复杂过滤器
var complexFilter = SearchFilter.Empty()
    .WithChatId(12345)
    .WithFromUserId(67890)
    .WithDateRange(DateTime.Now.AddDays(-30), DateTime.Now)
    .WithReplyFilter(true)
    .WithIncludedFileType("image")
    .WithRequiredTag("important");

// 应用过滤器
searchAggregate.UpdateFilter(complexFilter);
var filteredResult = await searchDomainService.ExecuteSearchAsync(searchAggregate);
```

### 5. 搜索查询分析

```csharp
// 分析查询
var query = new SearchQuery("hello +required -exclude field:value");
var analysis = await searchDomainService.AnalyzeQueryAsync(query);

Console.WriteLine($"原始查询: {analysis.OriginalQuery}");
Console.WriteLine($"优化查询: {analysis.OptimizedQuery}");
Console.WriteLine($"关键词: {string.Join(", ", analysis.Keywords)}");
Console.WriteLine($"排除词: {string.Join(", ", analysis.ExcludedTerms)}");
Console.WriteLine($"必需词: {string.Join(", ", analysis.RequiredTerms)}");
Console.WriteLine($"复杂度: {analysis.EstimatedComplexity}");
```

### 6. 搜索建议

```csharp
// 获取搜索建议
var suggestions = await searchDomainService.GetSearchSuggestionsAsync("hello", 5);

foreach (var suggestion in suggestions)
{
    Console.WriteLine($"建议: {suggestion}");
}
```

## 与现有代码的集成

### 1. SearchOption转换

```csharp
using TelegramSearchBot.Domain.Search.Adapters;
using TelegramSearchBot.Model;

// 从SearchOption创建SearchCriteria
var searchOption = new SearchOption
{
    Search = "hello world",
    SearchType = SearchType.InvertedIndex,
    Skip = 0,
    Take = 20,
    ChatId = 12345
};

var searchCriteria = searchOption.ToSearchCriteria();

// 执行搜索
var searchAggregate = SearchAggregate.Create(searchCriteria);
var result = await searchDomainService.ExecuteSearchAsync(searchAggregate);

// 转换回SearchOption
var updatedSearchOption = result.ToSearchOption(searchOption);
```

### 2. Message结果转换

```csharp
using TelegramSearchBot.Domain.Search.Adapters;
using TelegramSearchBot.Model.Data;

// 从Message创建SearchResultItem
var message = new Message
{
    MessageId = 123,
    GroupId = 456,
    Content = "Hello world",
    DateTime = DateTime.Now,
    FromUserId = 789
};

var resultItem = message.ToSearchResultItem(score: 0.8);

// 从SearchResultItem创建Message
var convertedMessage = resultItem.ToMessage();
```

## 事件处理

### 1. 领域事件订阅

```csharp
using TelegramSearchBot.Domain.Search.Events;

// 处理搜索会话开始事件
void OnSearchSessionStarted(SearchSessionStartedEvent @event)
{
    Console.WriteLine($"搜索会话开始: {@event.SearchId}");
    Console.WriteLine($"查询: {@event.Query}");
    Console.WriteLine($"搜索类型: {@event.SearchType}");
}

// 处理搜索完成事件
void OnSearchCompleted(SearchCompletedEvent @event)
{
    Console.WriteLine($"搜索完成: {@event.SearchId}");
    Console.WriteLine($"结果数量: {@event.Result.TotalResults}");
    Console.WriteLine($"执行时间: {@event.Result.ExecutionTime.TotalMilliseconds}ms");
}

// 处理搜索失败事件
void OnSearchFailed(SearchFailedEvent @event)
{
    Console.WriteLine($"搜索失败: {@event.SearchId}");
    Console.WriteLine($"错误: {@event.ErrorMessage}");
    Console.WriteLine($"异常类型: {@event.ExceptionType}");
}
```

## 测试示例

### 1. 单元测试

```csharp
using Xunit;
using Moq;
using TelegramSearchBot.Domain.Search.Tests;

public class SearchAggregateTests
{
    [Fact]
    public void Create_WithValidQuery_ShouldCreateAggregate()
    {
        // Arrange
        var query = "test query";

        // Act
        var aggregate = SearchAggregate.Create(query, SearchTypeValue.InvertedIndex());

        // Assert
        Assert.Equal(query, aggregate.Criteria.Query.Value);
        Assert.True(aggregate.IsActive);
        Assert.Single(aggregate.DomainEvents);
    }
}
```

### 2. 集成测试

```csharp
public class SearchServiceIntegrationTests
{
    [Fact]
    public async Task ExecuteSearch_WithRealData_ShouldReturnResults()
    {
        // Arrange
        var searchService = new SearchService(searchDomainService);
        var query = "test query";

        // Act
        var result = await searchService.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalResults >= 0);
    }
}
```

## 性能考虑

### 1. 查询优化

```csharp
// 使用优化的查询
var originalQuery = new SearchQuery("  test   query  ");
var optimizedQuery = searchDomainService.OptimizeQuery(originalQuery);

// 检查查询复杂度
var analysis = await searchDomainService.AnalyzeQueryAsync(optimizedQuery);
if (analysis.EstimatedComplexity > 0.8)
{
    Console.WriteLine("警告：查询复杂度过高，可能影响性能");
}
```

### 2. 缓存策略

```csharp
// 检查是否需要重新执行搜索
if (!searchAggregate.RequiresReexecution())
{
    // 使用缓存的结果
    var cachedResult = searchAggregate.LastResult;
}
else
{
    // 执行新的搜索
    var newResult = await searchDomainService.ExecuteSearchAsync(searchAggregate);
}
```

## 扩展性

### 1. 自定义搜索类型

```csharp
// 在SearchType枚举中添加新的搜索类型
public enum SearchType
{
    InvertedIndex = 0,
    Vector = 1,
    SyntaxSearch = 2,
    Hybrid = 3,
    Semantic = 4 // 新增语义搜索
}

// 在SearchDomainService中实现新的搜索逻辑
public async Task<SearchResult> SearchSemanticAsync(SearchCriteria criteria)
{
    // 实现语义搜索逻辑
}
```

### 2. 自定义过滤器

```csharp
// 扩展SearchFilter类
public class CustomSearchFilter : SearchFilter
{
    public double MinRelevanceScore { get; set; }
    public string[] CustomTags { get; set; }

    public CustomSearchFilter WithMinRelevanceScore(double score)
    {
        return new CustomSearchFilter(
            ChatId, FromUserId, StartDate, EndDate, HasReply,
            IncludedFileTypes, ExcludedFileTypes, RequiredTags, ExcludedTags)
        {
            MinRelevanceScore = score,
            CustomTags = CustomTags
        };
    }
}
```

## 总结

Search领域的DDD架构提供了以下优势：

1. **封装性**：搜索逻辑封装在聚合根中，外部代码不需要了解内部实现
2. **一致性**：通过值对象和领域服务确保业务规则的一致性
3. **可扩展性**：通过接口和事件机制支持功能扩展
4. **可测试性**：每个组件都可以独立测试
5. **兼容性**：通过适配器模式与现有代码保持兼容

这个架构为TelegramSearchBot的搜索功能提供了坚实的基础，支持未来的功能扩展和性能优化。