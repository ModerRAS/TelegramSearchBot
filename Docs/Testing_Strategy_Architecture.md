# TelegramSearchBot 分层测试策略文档

## 1. 测试架构概览

### 1.1 测试金字塔模型
```
            E2E测试 (10%)
           /            \
      集成测试 (20%)     性能测试 (5%)
     /          |       \
单元测试 (70%)   安全测试   兼容性测试
```

### 1.2 测试分层设计原则
- **单元测试**：快速、独立、无外部依赖
- **集成测试**：验证组件间协作，使用测试替身
- **端到端测试**：完整业务流程验证，接近生产环境

## 2. 单元测试策略

### 2.1 领域层测试
```csharp
// 测试领域实体行为
public class MessageEntityTests
{
    [Fact]
    public void Message_FromTelegramMessage_ShouldMapCorrectly()
    {
        // Arrange
        var telegramMessage = CreateTestTelegramMessage();
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.Should().BeEquivalentTo(expectedMessage, options => 
            options.Excluding(x => x.Id));
    }
}
```

### 2.2 应用服务测试
```csharp
// 测试业务逻辑和用例
public class MessageServiceTests
{
    [Fact]
    public async Task ProcessMessageAsync_ShouldIndexAndStoreMessage()
    {
        // Arrange
        var service = CreateMessageService();
        var message = CreateTestMessage();
        
        // Act
        var result = await service.ProcessMessageAsync(message);
        
        // Assert
        result.Should().BeTrue();
        await _messageRepository.Received(1).AddAsync(message);
        await _luceneManager.Received(1).IndexMessageAsync(message);
    }
}
```

### 2.3 基础设施测试
```csharp
// 测试数据访问和外部服务
public class MessageRepositoryTests
{
    [Fact]
    public async Task GetMessagesByGroupIdAsync_ShouldReturnFilteredMessages()
    {
        // Arrange
        var repository = CreateRepositoryWithTestData();
        
        // Act
        var result = await repository.GetMessagesByGroupIdAsync(100);
        
        // Assert
        result.Should().AllSatisfy(x => x.GroupId.Should().Be(100));
    }
}
```

## 3. 集成测试策略

### 3.1 数据库集成测试
```csharp
// 使用真实数据库但隔离测试数据
public class DataDbContextIntegrationTests
{
    [Fact]
    public async Task SaveMessage_ShouldPersistToDatabase()
    {
        // Arrange
        await using var context = CreateTestDbContext();
        var message = CreateTestMessage();
        
        // Act
        context.Messages.Add(message);
        await context.SaveChangesAsync();
        
        // Assert
        var savedMessage = await context.Messages.FindAsync(message.Id);
        savedMessage.Should().NotBeNull();
    }
}
```

### 3.2 服务集成测试
```csharp
// 测试服务间协作
public class MessageProcessingIntegrationTests
{
    [Fact]
    public async Task ProcessMessageWithAIServices_ShouldEnrichMessage()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var processor = serviceProvider.GetRequiredService<MessageProcessor>();
        var message = CreateTestMessage();
        
        // Act
        var result = await processor.ProcessMessageAsync(message);
        
        // Assert
        result.MessageExtensions.Should().Contain(x => x.ExtensionType == "OCR");
        result.MessageExtensions.Should().Contain(x => x.ExtensionType == "Vector");
    }
}
```

### 3.3 消息管道集成测试
```csharp
// 测试MediatR管道
public class MessagePipelineIntegrationTests
{
    [Fact]
    public async Task SendMessageNotification_ShouldTriggerAllHandlers()
    {
        // Arrange
        var mediator = CreateTestMediator();
        var notification = new TextMessageReceivedNotification(
            CreateTestMessage(), CreateTestBotClient());
        
        // Act
        await mediator.Publish(notification);
        
        // Assert
        await _vectorHandler.Received(1).Handle(notification);
        await _ocrHandler.Received(1).Handle(notification);
        await _asrHandler.Received(1).Handle(notification);
    }
}
```

## 4. 端到端测试策略

### 4.1 完整业务流程测试
```csharp
// 测试从接收到处理的完整流程
public class MessageEndToEndTests
{
    [Fact]
    public async Task ProcessTelegramMessageToEnd_ShouldCompleteWorkflow()
    {
        // Arrange
        var botService = CreateBotService();
        var update = CreateTestTelegramUpdate();
        
        // Act
        await botService.HandleUpdateAsync(update);
        
        // Assert
        // 验证消息已存储
        var storedMessage = await GetMessageFromDatabase(update.Message);
        storedMessage.Should().NotBeNull();
        
        // 验证消息已索引
        var searchResults = await SearchMessage(update.Message.Text);
        searchResults.Should().Contain(x => x.Id == storedMessage.Id);
        
        // 验证AI处理已完成
        var extensions = await GetMessageExtensions(storedMessage.Id);
        extensions.Should().NotBeEmpty();
    }
}
```

### 4.2 搜索功能端到端测试
```csharp
// 测试搜索功能完整性
public class SearchEndToEndTests
{
    [Fact]
    public async Task SearchMessages_ShouldReturnAccurateResults()
    {
        // Arrange
        await SetupTestData();
        var searchService = CreateSearchService();
        
        // Act
        var results = await searchService.SearchAsync("test query");
        
        // Assert
        results.Should().NotBeEmpty();
        results.Should().BeInDescendingOrder(x => x.Score);
        results.Should().OnlyContain(x => x.Message.Content.Contains("test"));
    }
}
```

## 5. 性能测试策略

### 5.1 搜索性能测试
```csharp
// 测试搜索性能
public class SearchPerformanceTests
{
    [Fact]
    public async Task SearchLargeDataset_ShouldMeetPerformanceRequirements()
    {
        // Arrange
        await SetupLargeDataset(10000); // 1万条消息
        var searchService = CreateSearchService();
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var results = await searchService.SearchAsync("performance test");
        stopwatch.Stop();
        
        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // 小于1秒
        results.Should().NotBeEmpty();
    }
}
```

### 5.2 AI处理性能测试
```csharp
// 测试AI服务性能
public class AIPerformanceTests
{
    [Fact]
    public async Task ProcessMessageWithAIServices_ShouldCompleteWithinTimeout()
    {
        // Arrange
        var processor = CreateMessageProcessor();
        var message = CreateTestMessageWithMedia();
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await processor.ProcessMessageAsync(message);
        stopwatch.Stop();
        
        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000); // 小于30秒
        result.Should().BeTrue();
    }
}
```

## 6. 测试数据管理策略

### 6.1 测试数据工厂
```csharp
// 统一的测试数据创建
public static class MessageTestDataFactory
{
    public static Message CreateTestMessage(Action<Message>? configure = null)
    {
        var message = new Message
        {
            Id = 1,
            GroupId = 100,
            MessageId = 1000,
            FromUserId = 1,
            Content = "Test message",
            DateTime = DateTime.UtcNow,
            MessageExtensions = new List<MessageExtension>()
        };
        
        configure?.Invoke(message);
        return message;
    }
    
    public static List<Message> CreateTestMessages(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateTestMessage(m => 
            {
                m.Id = i;
                m.MessageId = 1000 + i;
                m.Content = $"Test message {i}";
            }))
            .ToList();
    }
}
```

### 6.2 测试数据库初始化
```csharp
// 测试数据库设置
public class TestDatabaseSetup
{
    public static async Task<DataDbContext> CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        var context = new DataDbContext(options);
        await context.Database.EnsureCreatedAsync();
        
        return context;
    }
    
    public static async Task SeedTestData(DataDbContext context)
    {
        var messages = MessageTestDataFactory.CreateTestMessages(100);
        context.Messages.AddRange(messages);
        await context.SaveChangesAsync();
    }
}
```

## 7. Mock和Stub策略

### 7.1 外部服务Mock
```csharp
// Telegram Bot Client Mock
public static class TelegramBotClientMock
{
    public static Mock<ITelegramBotClient> Create()
    {
        var mock = new Mock<ITelegramBotClient>();
        
        mock.Setup(x => x.SendTextMessageAsync(
            It.IsAny<ChatId>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { MessageId = 1 });
            
        return mock;
    }
}
```

### 7.2 AI服务Mock
```csharp
// AI服务测试替身
public class MockOCRService : IPaddleOCRService
{
    public Task<string> ProcessImageAsync(string imagePath)
    {
        return Task.FromResult("Mocked OCR result");
    }
}
```

## 8. 测试覆盖率目标

### 8.1 覆盖率要求
- **单元测试覆盖率**：≥ 80%
- **集成测试覆盖率**：≥ 60%
- **关键路径覆盖率**：≥ 95%
- **异常处理覆盖率**：≥ 90%

### 8.2 覆盖率监控
```bash
# 生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"

# 查看覆盖率报告
reportgenerator -reports:coverage.xml -targetdir:coverage-report
```

## 9. 测试自动化流程

### 9.1 CI/CD集成
```yaml
# GitHub Actions示例
name: Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 9.0.x
    - name: Run tests
      run: |
        dotnet test --collect:"XPlat Code Coverage"
    - name: Generate coverage report
      run: |
        reportgenerator -reports:coverage.xml -targetdir:coverage-report
    - name: Upload coverage
      uses: codecov/codecov-action@v1
```

### 9.2 测试分类执行
```bash
# 只运行单元测试
dotnet test --filter "Category=Unit"

# 只运行集成测试
dotnet test --filter "Category=Integration"

# 只运行端到端测试
dotnet test --filter "Category=EndToEnd"

# 运行性能测试
dotnet test --filter "Category=Performance"
```

## 10. 测试最佳实践

### 10.1 测试命名约定
```csharp
// 好的命名
public class MessageServiceTests
{
    [Fact]
    public async Task ProcessMessageAsync_ValidMessage_ShouldReturnTrue()
    [Fact]
    public async Task ProcessMessageAsync_NullMessage_ShouldThrowException()
    [Fact]
    public async Task ProcessMessageAsync_DuplicateMessage_ShouldReturnFalse()
}
```

### 10.2 测试组织原则
- **AAA模式**：Arrange、Act、Assert
- **单一职责**：每个测试只验证一个行为
- **独立性**：测试之间不相互依赖
- **可重复性**：测试应该可以重复运行
- **快速执行**：单元测试应该在毫秒级完成

## 11. 测试工具和框架

### 11.1 核心测试框架
- **xUnit**：单元测试框架
- **Moq**：Mock对象框架
- **FluentAssertions**：断言库
- **AutoFixture**：测试数据生成

### 11.2 集成测试工具
- **TestContainers**：容器化测试
- **WireMock**：HTTP服务Mock
- **NBomber**：性能测试
- **coverlet**：覆盖率分析

## 12. 测试报告和监控

### 12.1 测试报告
- **单元测试报告**：xUnit XML格式
- **覆盖率报告**：HTML格式详细报告
- **性能测试报告**：NBomber报告
- **集成测试报告**：自定义HTML报告

### 12.2 质量门禁
- **代码覆盖率**：≥ 80%
- **测试通过率**：100%
- **性能基准**：满足SLA要求
- **代码质量**：符合SonarQube标准

这个测试策略文档提供了TelegramSearchBot项目的全面测试架构设计，涵盖了从单元测试到端到端测试的各个层面，确保项目的质量和稳定性。