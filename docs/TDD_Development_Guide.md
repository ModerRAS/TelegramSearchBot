# TelegramSearchBot TDD开发指南

## 📋 目录
1. [TDD概述](#tdd概述)
2. [开发环境配置](#开发环境配置)
3. [TDD核心流程](#tdd核心流程)
4. [各层TDD实践](#各层tdd实践)
5. [测试工具和框架](#测试工具和框架)
6. [最佳实践](#最佳实践)
7. [常见问题](#常见问题)

## TDD概述

### 什么是TDD
测试驱动开发（Test-Driven Development）是一种软件开发方法，要求在编写功能代码之前先编写测试代码。TDD遵循"红-绿-重构"的循环：

1. **红（Red）**：编写一个失败的测试
2. **绿（Green）**：编写最少的代码使测试通过
3. **重构（Refactor）**：优化代码，同时保持测试通过

### TDD的优势
- **提高代码质量**：确保所有代码都有测试覆盖
- **改善设计**：促进松耦合、高内聚的设计
- **减少调试时间**：快速定位问题
- **提供活文档**：测试用例作为代码的使用示例
- **增强信心**：重构时不用担心破坏现有功能

## 开发环境配置

### 必要工具
```bash
# 安装.NET SDK
dotnet --version

# 安装必要的NuGet包
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.NET.Test.Sdk
```

### 项目结构
```
TelegramSearchBot.sln
├── TelegramSearchBot.Domain/          # 领域层
├── TelegramSearchBot.Application/     # 应用层
├── TelegramSearchBot.Infrastructure/  # 基础设施层
├── TelegramSearchBot.Data/           # 数据层
├── TelegramSearchBot.Test/           # 测试项目
│   ├── Domain/
│   ├── Application/
│   ├── Integration/
│   └── Performance/
```

## TDD核心流程

### 1. 编写失败的测试（红）
```csharp
[Fact]
public void CreateMessage_WithValidData_ShouldSucceed()
{
    // Arrange
    var messageId = new MessageId(123, 456);
    var content = new MessageContent("Hello World");
    var metadata = new MessageMetadata("user1", DateTime.UtcNow);
    
    // Act
    var message = new MessageAggregate(messageId, content, metadata);
    
    // Assert
    message.Should().NotBeNull();
    message.Id.Should().Be(messageId);
}
```

### 2. 编写最少代码使测试通过（绿）
```csharp
public class MessageAggregate
{
    public MessageId Id { get; }
    public MessageContent Content { get; }
    public MessageMetadata Metadata { get; }
    
    public MessageAggregate(MessageId id, MessageContent content, MessageMetadata metadata)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}
```

### 3. 重构代码
```csharp
// 添加领域事件
public class MessageAggregate
{
    private readonly List<IDomainEvent> _domainEvents = new();
    
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    public MessageAggregate(MessageId id, MessageContent content, MessageMetadata metadata)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        
        _domainEvents.Add(new MessageCreatedEvent(id, content, metadata));
    }
    
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

## 各层TDD实践

### 1. 领域层（Domain Layer）

#### 值对象测试
```csharp
public class MessageIdTests
{
    [Theory]
    [InlineData(0, 1)]      // 无效的ChatId
    [InlineData(1, 0)]      // 无效的MessageId
    [InlineData(-1, 1)]     // 负的ChatId
    [InlineData(1, -1)]     // 负的MessageId
    public void CreateMessageId_WithInvalidIds_ShouldThrowException(long chatId, int messageId)
    {
        // Act
        Action act = () => new MessageId(chatId, messageId);
        
        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Invalid message identifier*");
    }
    
    [Fact]
    public void MessageId_Equals_ShouldWorkCorrectly()
    {
        // Arrange
        var id1 = new MessageId(123, 456);
        var id2 = new MessageId(123, 456);
        var id3 = new MessageId(123, 789);
        
        // Act & Assert
        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
        (id1 == id2).Should().BeTrue();
        (id1 != id3).Should().BeTrue();
    }
}
```

#### 聚合根测试
```csharp
public class MessageAggregateTests
{
    [Fact]
    public void UpdateContent_WithValidContent_ShouldUpdateAndPublishEvent()
    {
        // Arrange
        var message = CreateValidMessage();
        var newContent = new MessageContent("Updated content");
        
        // Act
        message.UpdateContent(newContent);
        
        // Assert
        message.Content.Should().Be(newContent);
        message.DomainEvents.Should().Contain(e => 
            e is MessageContentUpdatedEvent);
    }
    
    [Fact]
    public void UpdateContent_WithSameContent_ShouldNotPublishEvent()
    {
        // Arrange
        var message = CreateValidMessage();
        var sameContent = message.Content;
        
        // Act
        message.UpdateContent(sameContent);
        
        // Assert
        message.DomainEvents.Should().NotContain(e => 
            e is MessageContentUpdatedEvent);
    }
}
```

### 2. 应用层（Application Layer）

#### 命令处理器测试
```csharp
public class CreateMessageCommandHandlerTests
{
    private readonly Mock<IMessageRepository> _messageRepositoryMock;
    private readonly Mock<IMessageApplicationService> _messageServiceMock;
    private readonly CreateMessageCommandHandler _handler;
    
    public CreateMessageCommandHandlerTests()
    {
        _messageRepositoryMock = new Mock<IMessageRepository>();
        _messageServiceMock = new Mock<IMessageApplicationService>();
        _handler = new CreateMessageCommandHandler(
            _messageRepositoryMock.Object,
            _messageServiceMock.Object);
    }
    
    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateMessage()
    {
        // Arrange
        var command = new CreateMessageCommand(
            123, 456, "Test message", "user1", DateTime.UtcNow);
        
        // Act
        var result = await _handler.Handle(command, CancellationToken.None);
        
        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        _messageRepositoryMock.Verify(r => 
            r.AddAsync(It.IsAny<MessageAggregate>()), Times.Once);
    }
}
```

#### 查询处理器测试
```csharp
public class GetMessageQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingMessage_ShouldReturnMessage()
    {
        // Arrange
        var query = new GetMessageQuery(123, 456);
        var expectedMessage = CreateTestMessage();
        
        _messageRepositoryMock
            .Setup(r => r.GetByIdAsync(query.ChatId, query.MessageId))
            .ReturnsAsync(expectedMessage);
        
        // Act
        var result = await _handler.Handle(query, CancellationToken.None);
        
        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedMessage);
    }
}
```

### 3. 集成测试

```csharp
[Collection("DatabaseCollection")]
public class MessageProcessingIntegrationTests
{
    private readonly TestDatabaseFixture _fixture;
    
    public MessageProcessingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task ProcessMessage_EndToEnd_ShouldWorkCorrectly()
    {
        // Arrange
        var processor = new MessageProcessingPipeline(
            _fixture.MessageRepository,
            _fixture.SearchService,
            _fixture.Logger);
        
        var message = CreateTestMessage();
        
        // Act
        await processor.ProcessAsync(message);
        
        // Assert
        var storedMessage = await _fixture.MessageRepository
            .GetByIdAsync(message.Id.ChatId, message.Id.MessageId);
        storedMessage.Should().NotBeNull();
        
        var searchResults = await _fixture.SearchService
            .SearchAsync("test content");
        searchResults.Should().Contain(m => m.Id.Equals(message.Id));
    }
}
```

## 测试工具和框架

### 1. xUnit
- **Fact**：单个测试用例
- **Theory**：参数化测试
- **InlineData**：提供测试数据
- **ClassData**：复杂测试数据

### 2. Moq
```csharp
// 创建Mock
var repositoryMock = new Mock<IMessageRepository>();

// 设置方法行为
repositoryMock
    .Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<int>()))
    .ReturnsAsync((long chatId, int messageId) => 
        CreateTestMessage(chatId, messageId));

// 验证方法调用
repositoryMock.Verify(r => 
    r.AddAsync(It.IsAny<MessageAggregate>()), Times.Once);
```

### 3. Fluent Assertions
```csharp
// 对象比较
result.Should().BeEquivalentTo(expected);

// 异常断言
Action act = () => service.DoSomething();
act.Should().Throw<InvalidOperationException>()
   .WithMessage("*Something went wrong*");

// 异步断言
await func.Should().ThrowAsync<Exception>();

// 集合断言
collection.Should().HaveCount(3);
collection.Should().Contain(item => item.Name == "Test");
```

## 最佳实践

### 1. 测试命名约定
```csharp
// 方法名：UnitOfWork_Scenario_ExpectedResult
[Fact]
public void CreateMessage_WithNullContent_ShouldThrowException()
[Fact]
public void UpdateContent_WithValidContent_ShouldUpdateAndPublishEvent()
[Fact]
public async Task ProcessMessage_WhenRepositoryFails_ShouldReturnError()
```

### 2. 测试数据创建
```csharp
public static class MessageTestDataFactory
{
    public static MessageAggregate CreateValidMessage(
        long chatId = 123,
        int messageId = 456,
        string content = "Test message")
    {
        return new MessageAggregate(
            new MessageId(chatId, messageId),
            new MessageContent(content),
            new MessageMetadata("user1", DateTime.UtcNow));
    }
    
    public static CreateMessageCommand CreateValidCommand()
    {
        return new CreateMessageCommand(
            123, 456, "Test message", "user1", DateTime.UtcNow);
    }
}
```

### 3. 测试隔离
```csharp
public class MessageServiceTests : IDisposable
{
    private readonly IMessageRepository _repository;
    private readonly IMessageService _service;
    private readonly DbContext _context;
    
    public MessageServiceTests()
    {
        // 每个测试使用新的数据库实例
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TestDbContext(options);
        _repository = new MessageRepository(_context);
        _service = new MessageService(_repository);
    }
    
    public void Dispose()
    {
        _context.Dispose();
    }
}
```

### 4. 测试覆盖目标
- **单元测试**：80-90%
- **集成测试**：关键业务流程
- **端到端测试**：核心用户场景

## 常见问题

### 1. 如何测试私有方法？
**答**：不要直接测试私有方法。通过公共API测试其行为。如果私有方法很复杂，考虑将其提取为独立的类。

### 2. 如何处理外部依赖？
**答**：使用Mock对象模拟外部依赖。对于集成测试，可以使用测试容器或内存版本。

### 3. 测试运行太慢怎么办？
**答**：
- 减少数据库访问
- 使用内存数据库进行单元测试
- 并行运行测试
- 只在CI中运行完整的测试套件

### 4. 如何测试异步代码？
```csharp
[Fact]
public async Task AsyncMethod_ShouldWorkCorrectly()
{
    // Arrange
    var service = new MessageService(repositoryMock.Object);
    
    // Act
    Func<Task> act = async () => await service.ProcessAsync(message);
    
    // Assert
    await act.Should().NotThrowAsync();
    await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1));
}
```

## 总结

TDD是一种强大的开发方法，能够显著提高代码质量和开发效率。在TelegramSearchBot项目中，我们已经：

1. 建立了完整的测试框架
2. 实现了领域层的TDD开发
3. 创建了应用层的TDD实践
4. 建立了集成测试流程

继续坚持TDD实践，确保新功能都先写测试，这将帮助我们构建一个高质量、可维护的系统。