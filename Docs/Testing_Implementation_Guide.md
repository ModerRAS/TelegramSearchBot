# TelegramSearchBot 测试实施建议和最佳实践

## 1. 测试用例示例

### 1.1 实体测试示例 - Message领域模型

#### 1.1.1 消息转换测试
```csharp
[Unit/Domain/Model/Message/MessageConversionTests.cs]
public class MessageConversionTests : DomainTestBase
{
    [Theory]
    [InlineData("简单的文本消息", "简单的文本消息")]
    [InlineData("包含特殊字符的消息!@#$%", "包含特殊字符的消息!@#$%")]
    [InlineData("", "")] // 空消息
    [InlineData("  ", "  ")] // 空格消息
    [InlineData("很长的消息内容" + string.Join("", Enumerable.Range(1, 1000).Select(i => "测试")), "很长的消息内容...")] // 长消息
    public void FromTelegramMessage_VariousContentTypes_ShouldHandleCorrectly(string inputContent, string expectedContent)
    {
        // Arrange
        var telegramMessage = MessageTestDataFactory.CreateTelegramMessage(m =>
        {
            m.Text = inputContent;
            m.MessageId = 1001;
        });
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.Should().NotBeNull();
        result.Content.Should().Be(expectedContent);
        result.MessageId.Should().Be(1001);
        result.GroupId.Should().Be(100);
        result.FromUserId.Should().Be(1);
    }
    
    [Fact]
    public void FromTelegramMessage_ComplexMessageWithReply_ShouldMapAllFields()
    {
        // Arrange
        var telegramMessage = new Telegram.Bot.Types.Message
        {
            MessageId = 1002,
            Chat = new Chat { Id = 200, Title = "Test Group" },
            From = new User { Id = 2, Username = "testuser", FirstName = "Test", LastName = "User" },
            Text = "这是一条回复消息",
            Date = DateTime.UtcNow,
            ReplyToMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1001,
                From = new User { Id = 1, Username = "originaluser" }
            },
            ForwardFrom = new User { Id = 3, Username = "forwarduser" },
            Entities = new List<MessageEntity>
            {
                new MessageEntity { Type = MessageEntityType.Bold, Offset = 0, Length = 2 }
            }
        };
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.Should().NotBeNull();
        result.MessageId.Should().Be(1002);
        result.GroupId.Should().Be(200);
        result.FromUserId.Should().Be(2);
        result.Content.Should().Be("这是一条回复消息");
        result.ReplyToUserId.Should().Be(1);
        result.ReplyToMessageId.Should().Be(1001);
    }
}
```

#### 1.1.2 消息验证测试
```csharp
[Unit/Domain/Model/Message/MessageValidationTests.cs]
public class MessageValidationTests : DomainTestBase
{
    [Theory]
    [InlineData(0, 1000, 1, "内容")] // GroupId为0
    [InlineData(100, 0, 1, "内容")] // MessageId为0
    [InlineData(100, 1000, 0, "内容")] // FromUserId为0
    [InlineData(100, 1000, 1, "")] // Content为空
    [InlineData(100, 1000, 1, "   ")] // Content为空格
    [InlineData(100, 1000, 1, null)] // Content为null
    public void Validate_InvalidMessage_ShouldThrowDomainException(int groupId, int messageId, int fromUserId, string content)
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.GroupId = groupId;
            m.MessageId = messageId;
            m.FromUserId = fromUserId;
            m.Content = content;
        });
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>();
    }
    
    [Fact]
    public void Validate_MessageTooLong_ShouldThrowDomainException()
    {
        // Arrange
        var longContent = new string('A', 5000); // 超过限制的长度
        var message = MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.Content = longContent;
        });
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>()
            .WithMessage("*Message content too long*");
    }
    
    [Fact]
    public void Validate_MessageWithInvalidCharacters_ShouldThrowDomainException()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.Content = "包含非法字符\0\1\2的消息";
        });
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>()
            .WithMessage("*Message contains invalid characters*");
    }
}
```

### 1.2 服务测试示例 - MessageService

#### 1.2.1 消息处理服务测试
```csharp
[Unit/Application/Service/Message/MessageServiceProcessingTests.cs]
public class MessageServiceProcessingTests : UnitTestBase
{
    private readonly Mock<IMessageRepository> _messageRepository;
    private readonly Mock<ILuceneManager> _luceneManager;
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<ILogger<MessageService>> _logger;
    private readonly MessageService _messageService;
    
    public MessageServiceProcessingTests(ITestOutputHelper output) : base(output)
    {
        _messageRepository = CreateMock<IMessageRepository>();
        _luceneManager = CreateMock<ILuceneManager>();
        _mediator = CreateMock<IMediator>();
        _logger = CreateMock<ILogger<MessageService>>();
        
        _messageService = new MessageService(
            _logger.Object,
            _luceneManager.Object,
            null!, null!, _mediator.Object);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_NewMessage_ShouldTriggerCompleteWorkflow()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        var cancellationToken = CancellationToken.None;
        
        _messageRepository.Setup(x => x.ExistsAsync(message.GroupId, message.MessageId))
            .ReturnsAsync(false);
        _messageRepository.Setup(x => x.AddAsync(message))
            .Returns(Task.CompletedTask);
        _luceneManager.Setup(x => x.IndexMessageAsync(message))
            .Returns(Task.CompletedTask);
        _mediator.Setup(x => x.Publish(
            It.IsAny<TextMessageReceivedNotification>(),
            cancellationToken))
            .Returns(Task.CompletedTask);
        
        // Act
        var result = await _messageService.ProcessMessageAsync(message, cancellationToken);
        
        // Assert
        result.Should().BeTrue();
        
        // 验证工作流程
        _messageRepository.Verify(x => x.ExistsAsync(message.GroupId, message.MessageId), Times.Once);
        _messageRepository.Verify(x => x.AddAsync(message), Times.Once);
        _luceneManager.Verify(x => x.IndexMessageAsync(message), Times.Once);
        _mediator.Verify(x => x.Publish(
            It.Is<TextMessageReceivedNotification>(n => 
                n.Message == message && 
                n.BotClient == null),
            cancellationToken), Times.Once);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_DuplicateMessage_ShouldSkipProcessing()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        
        _messageRepository.Setup(x => x.ExistsAsync(message.GroupId, message.MessageId))
            .ReturnsAsync(true);
        
        // Act
        var result = await _messageService.ProcessMessageAsync(message);
        
        // Assert
        result.Should().BeFalse();
        
        // 验证没有进行后续处理
        _messageRepository.Verify(x => x.AddAsync(message), Times.Never);
        _luceneManager.Verify(x => x.IndexMessageAsync(message), Times.Never);
        _mediator.Verify(x => x.Publish(
            It.IsAny<TextMessageReceivedNotification>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_RepositoryFails_ShouldThrowAndLog()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        var expectedException = new DatabaseException("Database connection failed");
        
        _messageRepository.Setup(x => x.ExistsAsync(message.GroupId, message.MessageId))
            .ReturnsAsync(false);
        _messageRepository.Setup(x => x.AddAsync(message))
            .ThrowsAsync(expectedException);
        
        // Act & Assert
        var action = async () => await _messageService.ProcessMessageAsync(message);
        await action.Should().ThrowAsync<DatabaseException>()
            .WithMessage("Database connection failed");
        
        // 验证错误日志
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to process message")),
                It.IsAny<DatabaseException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

#### 1.2.2 消息搜索服务测试
```csharp
[Unit/Application/Service/Message/MessageServiceSearchTests.cs]
public class MessageServiceSearchTests : UnitTestBase
{
    private readonly Mock<IMessageRepository> _messageRepository;
    private readonly Mock<ILuceneManager> _luceneManager;
    private readonly Mock<ILogger<MessageService>> _logger;
    private readonly MessageService _messageService;
    
    public MessageServiceSearchTests(ITestOutputHelper output) : base(output)
    {
        _messageRepository = CreateMock<IMessageRepository>();
        _luceneManager = CreateMock<ILuceneManager>();
        _logger = CreateMock<ILogger<MessageService>>();
        
        _messageService = new MessageService(
            _logger.Object,
            _luceneManager.Object,
            null!, null!, null!);
    }
    
    [Theory]
    [InlineData("算法")]
    [InlineData("数据结构")]
    [InlineData("编程开发")]
    [InlineData("机器学习")]
    public async Task SearchMessagesAsync_ValidQuery_ShouldReturnResults(string query)
    {
        // Arrange
        var groupId = 100;
        var expectedResults = new List<SearchResult>
        {
            new SearchResult 
            { 
                Message = MessageTestDataFactory.CreateValidMessage(m => m.Content = $"关于{query}的讨论"),
                Score = 0.9f
            },
            new SearchResult 
            { 
                Message = MessageTestDataFactory.CreateValidMessage(m => m.Content = $"{query}相关的话题"),
                Score = 0.7f
            }
        };
        
        _luceneManager.Setup(x => x.SearchAsync(query))
            .ReturnsAsync(expectedResults);
        
        // Act
        var result = await _messageService.SearchMessagesAsync(query, groupId);
        
        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(x => x.Score);
        result.Should().OnlyContain(x => x.Message.Content.Contains(query));
        
        _luceneManager.Verify(x => x.SearchAsync(query), Times.Once);
    }
    
    [Fact]
    public async Task SearchMessagesAsync_EmptyResults_ShouldReturnEmptyList()
    {
        // Arrange
        var query = "不存在的搜索词";
        _luceneManager.Setup(x => x.SearchAsync(query))
            .ReturnsAsync(new List<SearchResult>());
        
        // Act
        var result = await _messageService.SearchMessagesAsync(query, 100);
        
        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task SearchMessagesAsync_InvalidQuery_ShouldThrowArgumentException(string invalidQuery)
    {
        // Arrange, Act & Assert
        var action = async () => await _messageService.SearchMessagesAsync(invalidQuery!, 100);
        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Query cannot be empty or whitespace*");
    }
}
```

### 1.3 集成测试示例 - Message处理管道

#### 1.3.1 端到端消息处理测试
```csharp
[EndToEnd/MessageProcessing/MessageEndToEndTests.cs]
public class MessageEndToEndTests : IntegrationTestBase
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly Mock<ITelegramBotClient> _botClient;
    private readonly Mock<IPaddleOCRService> _ocrService;
    private readonly Mock<IAutoASRService> _asrService;
    private readonly Mock<IGeneralLLMService> _llmService;
    
    public MessageEndToEndTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _messageProcessor = GetService<IMessageProcessor>();
        _botClient = MockFactory.CreateTelegramBotClientMock();
        _ocrService = MockFactory.CreateOCRServiceMock();
        _asrService = MockFactory.CreateASRServiceMock();
        _llmService = MockFactory.CreateLLMServiceMock();
        
        // 注册Mock服务
        var services = new ServiceCollection();
        services.AddSingleton(_ocrService.Object);
        services.AddSingleton(_asrService.Object);
        services.AddSingleton(_llmService.Object);
        
        ServiceProvider = services.BuildServiceProvider();
    }
    
    [Fact]
    public async Task ProcessTextMessageToEnd_ShouldCompleteFullWorkflow()
    {
        // Arrange
        await ClearDatabaseAsync();
        var update = new Update
        {
            Id = 1,
            Message = MessageTestDataFactory.CreateTelegramMessage(m =>
            {
                m.Text = "这是一条测试消息";
                m.MessageId = 1001;
            })
        };
        
        // Act
        var result = await _messageProcessor.ProcessUpdateAsync(update, _botClient.Object);
        
        // Assert
        result.Should().BeTrue();
        
        // 验证消息存储
        await using var context = GetService<DataDbContext>();
        var storedMessage = await context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == 1001);
        storedMessage.Should().NotBeNull();
        storedMessage!.Content.Should().Be("这是一条测试消息");
        
        // 验证向量生成
        _llmService.Verify(x => x.GenerateEmbeddingAsync("这是一条测试消息"), Times.Once);
        
        // 验证消息索引
        var luceneManager = GetService<LuceneManager>();
        // 这里可以添加对Lucene索引的验证
    }
    
    [Fact]
    public async Task ProcessPhotoMessageToEnd_ShouldTriggerOCRAndVectorGeneration()
    {
        // Arrange
        await ClearDatabaseAsync();
        var update = new Update
        {
            Id = 2,
            Message = MessageTestDataFactory.CreateTelegramPhotoMessage(m =>
            {
                m.Caption = "查看这张图片";
                m.MessageId = 1002;
                m.Photo = new List<PhotoSize>
                {
                    new PhotoSize
                    {
                        FileId = "photo_file_123",
                        FileUniqueId = "photo_unique_123",
                        Width = 1280,
                        Height = 720,
                        FileSize = 102400
                    }
                };
            })
        };
        
        _botClient.Setup(x => x.GetFileAsync("photo_file_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new File { FilePath = "photos/photo_123.jpg" });
        
        // Act
        var result = await _messageProcessor.ProcessUpdateAsync(update, _botClient.Object);
        
        // Assert
        result.Should().BeTrue();
        
        // 验证消息存储
        await using var context = GetService<DataDbContext>();
        var storedMessage = await context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == 1002);
        storedMessage.Should().NotBeNull();
        storedMessage!.Content.Should().Be("查看这张图片");
        
        // 验证OCR处理
        _ocrService.Verify(x => x.ProcessImageAsync(It.Is<string>(path => path.Contains("photo_123.jpg"))), Times.Once);
        
        // 验证向量生成
        _llmService.Verify(x => x.GenerateEmbeddingAsync("查看这张图片"), Times.Once);
    }
}
```

### 1.4 性能测试示例

#### 1.4.1 消息存储性能测试
```csharp
[Performance/Message/MessageStoragePerformanceTests.cs]
public class MessageStoragePerformanceTests : IntegrationTestBase
{
    private readonly IMessageRepository _repository;
    
    public MessageStoragePerformanceTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _repository = GetService<IMessageRepository>();
    }
    
    [Theory]
    [InlineData(100)]      // 小批量
    [InlineData(1000)]     // 中批量
    [InlineData(5000)]     // 大批量
    public async Task AddMessages_BulkInsert_ShouldScaleLinearly(int messageCount)
    {
        // Arrange
        await ClearDatabaseAsync();
        var messages = MessageTestDataFactory.CreateMessageList(messageCount);
        var stopwatch = new Stopwatch();
        
        // Act
        stopwatch.Start();
        foreach (var message in messages)
        {
            await _repository.AddAsync(message);
        }
        stopwatch.Stop();
        
        // Assert
        var durationPerMessage = stopwatch.ElapsedMilliseconds / (double)messageCount;
        Output.WriteLine($"Added {messageCount} messages in {stopwatch.ElapsedMilliseconds}ms ({durationPerMessage:F2}ms per message)");
        
        // 性能断言
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(messageCount * 10); // 每条消息不超过10ms
        durationPerMessage.Should().BeLessThan(5); // 平均每条消息不超过5ms
    }
    
    [Fact]
    public async Task GetMessages_LargeDataset_ShouldMaintainPerformance()
    {
        // Arrange
        await ClearDatabaseAsync();
        await DatabaseFixture.Context.AddTestMessagesAsync(10000);
        var stopwatch = new Stopwatch();
        
        // Act
        stopwatch.Start();
        var results = await _repository.GetMessagesByGroupIdAsync(100);
        stopwatch.Stop();
        
        // Assert
        results.Should().HaveCount(10000);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // 1秒内完成
        
        Output.WriteLine($"Retrieved {results.Count} messages in {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

## 2. 测试实施建议

### 2.1 测试代码组织原则

#### 2.1.1 测试文件命名规范
```csharp
// 好的命名
MessageServiceTests.cs           // 服务测试
MessageEntityTests.cs           // 实体测试
MessageRepositoryTests.cs       // 仓储测试
MessageProcessingTests.cs       // 处理管道测试
MessagePerformanceTests.cs      // 性能测试

// 避免的命名
MessageTests.cs                 // 太宽泛
TestMessage.cs                  // 前缀错误
MessageTest.cs                  // 单数形式
MessageTesting.cs               // 动名词形式
```

#### 2.1.2 测试类组织结构
```csharp
// 按功能分组
public class MessageServiceTests
{
    public class ProcessMessageAsync
    {
        public class ValidMessage { }
        public class DuplicateMessage { }
        public class InvalidMessage { }
        public class ErrorHandling { }
    }
    
    public class SearchMessagesAsync
    {
        public class ValidQuery { }
        public class EmptyResults { }
        public class InvalidQuery { }
    }
}
```

### 2.2 测试数据管理策略

#### 2.2.1 测试数据工厂模式
```csharp
// 推荐的方式 - 使用Builder模式
public class MessageBuilder
{
    private Message _message = new Message();
    
    public MessageBuilder WithId(int id)
    {
        _message.Id = id;
        return this;
    }
    
    public MessageBuilder WithContent(string content)
    {
        _message.Content = content;
        return this;
    }
    
    public MessageBuilder FromUser(int userId)
    {
        _message.FromUserId = userId;
        return this;
    }
    
    public MessageBuilder InGroup(int groupId)
    {
        _message.GroupId = groupId;
        return this;
    }
    
    public MessageBuilder WithReplyTo(int replyToUserId, int replyToMessageId)
    {
        _message.ReplyToUserId = replyToUserId;
        _message.ReplyToMessageId = replyToMessageId;
        return this;
    }
    
    public Message Build() => _message;
}

// 使用示例
var message = new MessageBuilder()
    .WithId(1)
    .WithContent("测试消息")
    .FromUser(1)
    .InGroup(100)
    .WithReplyTo(2, 999)
    .Build();
```

#### 2.2.2 测试数据清理策略
```csharp
public class DatabaseCleanup
{
    public static async Task CleanAllData(DataDbContext context)
    {
        // 按依赖关系顺序删除
        context.MessageExtensions.RemoveRange(context.MessageExtensions);
        context.Messages.RemoveRange(context.Messages);
        context.UserData.RemoveRange(context.UserData);
        context.GroupData.RemoveRange(context.GroupData);
        
        await context.SaveChangesAsync();
    }
    
    public static async Task CleanMessagesByGroup(DataDbContext context, int groupId)
    {
        var messages = await context.Messages
            .Where(m => m.GroupId == groupId)
            .ToListAsync();
            
        var messageIds = messages.Select(m => m.Id).ToList();
        
        context.MessageExtensions.RemoveRange(
            context.MessageExtensions.Where(me => messageIds.Contains(me.MessageId)));
        context.Messages.RemoveRange(messages);
        
        await context.SaveChangesAsync();
    }
}
```

### 2.3 Mock策略最佳实践

#### 2.3.1 Mock设置原则
```csharp
// 好的Mock设置
var mockRepository = new Mock<IMessageRepository>();
mockRepository
    .Setup(x => x.GetByIdAsync(1))
    .ReturnsAsync(expectedMessage);
    
mockRepository
    .Setup(x => x.AddAsync(It.IsAny<Message>()))
    .Returns(Task.CompletedTask);
    
// 避免的Mock设置
mockRepository
    .Setup(x => x.GetByIdAsync(It.IsAny<int>())) // 太宽泛
    .ReturnsAsync(expectedMessage);
```

#### 2.3.2 Mock验证最佳实践
```csharp
// 好的验证
mockRepository.Verify(
    x => x.AddAsync(It.Is<Message>(m => m.Content == "测试内容")),
    Times.Once);
    
// 避免的验证
mockRepository.Verify(
    x => x.AddAsync(It.IsAny<Message>()), // 太宽泛
    Times.Once);
```

### 2.4 异常测试策略

#### 2.4.1 异常测试模式
```csharp
[Fact]
public async Task ProcessMessageAsync_RepositoryThrows_ShouldHandleException()
{
    // Arrange
    var message = MessageTestDataFactory.CreateValidMessage();
    var expectedException = new DatabaseException("Connection failed");
    
    _messageRepository
        .Setup(x => x.AddAsync(message))
        .ThrowsAsync(expectedException);
    
    // Act & Assert
    var action = async () => await _messageService.ProcessMessageAsync(message);
    await action.Should().ThrowAsync<DatabaseException>()
        .WithMessage("Connection failed");
    
    // 验证日志记录
    _logger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to process message")),
            It.IsAny<DatabaseException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

### 2.5 测试配置管理

#### 2.5.1 测试配置文件
```json
// testsettings.json
{
  "TestSettings": {
    "Database": {
      "UseInMemory": true,
      "SeedTestData": true,
      "CleanupAfterTest": true
    },
    "Performance": {
      "MaxDuration": "00:05:00",
      "WarningThreshold": "00:01:00"
    },
    "Logging": {
      "LogLevel": "Information",
      "LogToFile": false
    }
  }
}
```

#### 2.5.2 测试配置类
```csharp
public class TestConfiguration
{
    public DatabaseSettings Database { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public class DatabaseSettings
{
    public bool UseInMemory { get; set; } = true;
    public bool SeedTestData { get; set; } = true;
    public bool CleanupAfterTest { get; set; } = true;
}

public class PerformanceSettings
{
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMinutes(1);
}

public class LoggingSettings
{
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool LogToFile { get; set; } = false;
}
```

## 3. 测试执行和报告

### 3.1 测试分类执行
```bash
# 执行所有单元测试
dotnet test --filter "Category=Unit"

# 执行所有集成测试
dotnet test --filter "Category=Integration"

# 执行Message相关测试
dotnet test --filter "Message"

# 执行性能测试
dotnet test --filter "Category=Performance"

# 生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
```

### 3.2 测试报告生成
```powershell
# 生成HTML覆盖率报告
reportgenerator -reports:TestResults/coverage.xml -targetdir:CoverageReport -reporttypes:Html

# 生成测试结果报告
dotnet test --logger trx --results-directory TestResults
```

### 3.3 持续集成配置
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
    - name: Restore dependencies
      run: dotnet restore
    - name: Build
      run: dotnet build --no-restore
    - name: Run unit tests
      run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage" --filter "Category=Unit"
    - name: Run integration tests
      run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage" --filter "Category=Integration"
    - name: Generate coverage report
      run: |
        reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:Html
    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v1
```

## 4. 测试质量保证

### 4.1 测试质量检查清单

#### 4.1.1 单元测试检查清单
- [ ] 测试命名是否符合规范
- [ ] 是否遵循AAA模式
- [ ] 是否有足够的断言
- [ ] 是否测试了正常情况
- [ ] 是否测试了边界情况
- [ ] 是否测试了异常情况
- [ ] Mock对象是否正确设置
- [ ] 测试是否独立（不依赖其他测试）
- [ ] 测试是否可重复执行
- [ ] 测试执行速度是否合理

#### 4.1.2 集成测试检查清单
- [ ] 测试环境是否正确配置
- [ ] 测试数据是否正确设置和清理
- [ ] 是否测试了组件间的交互
- [ ] 是否测试了数据库操作
- [ ] 是否测试了外部服务调用
- [ ] 是否测试了错误处理
- [ ] 测试是否模拟了真实场景
- [ ] 测试是否有适当的超时设置
- [ ] 测试结果是否正确验证
- [ ] 测试是否有适当的日志记录

### 4.2 测试覆盖率目标

#### 4.2.1 覆盖率分级标准
```csharp
// 关键代码路径 - 100% 覆盖率
[CriticalCoverage(100)]
public class MessageService
{
    public async Task<bool> ProcessMessageAsync(Message message)
    {
        // 关键业务逻辑必须100%覆盖
    }
}

// 重要业务逻辑 - 90% 覆盖率
[ImportantCoverage(90)]
public class MessageRepository
{
    public async Task<Message> GetByIdAsync(int id)
    {
        // 重要数据访问逻辑
    }
}

// 一般工具类 - 80% 覆盖率
[StandardCoverage(80)]
public class MessageExtensions
{
    public static string ToDisplayString(this Message message)
    {
        // 工具方法
    }
}
```

#### 4.2.2 覆盖率监控
```bash
# 检查覆盖率
dotnet test --collect:"XPlat Code Coverage"

# 生成覆盖率报告
reportgenerator -reports:coverage.xml -targetdir:coverage-report

# 设置覆盖率阈值
dotnet test --collect:"XPlat Code Coverage" --threshold 80
```

## 5. 测试维护和优化

### 5.1 测试代码重构

#### 5.1.1 识别测试代码坏味道
```csharp
// 坏味道：重复的测试设置
public class MessageServiceTests1
{
    [Fact]
    public void Test1()
    {
        var repository = new Mock<IMessageRepository>();
        var logger = new Mock<ILogger<MessageService>>();
        var service = new MessageService(logger.Object, null!, null!, null!, null!);
        // 重复的设置代码
    }
}

// 重构：提取到基类
public class MessageServiceTestsBase : UnitTestBase
{
    protected MessageService CreateService(
        IMock<IMessageRepository>? repository = null,
        IMock<ILogger<MessageService>>? logger = null)
    {
        return new MessageService(
            (logger ?? CreateMock<ILogger<MessageService>>()).Object,
            null!, null!, null!, null!);
    }
}
```

#### 5.1.2 测试数据管理优化
```csharp
// 坏味道：硬编码的测试数据
[Fact]
public void Test()
{
    var message = new Message
    {
        Id = 1,
        GroupId = 100,
        MessageId = 1000,
        FromUserId = 1,
        Content = "测试消息"
    };
}

// 重构：使用测试数据工厂
[Fact]
public void Test()
{
    var message = MessageTestDataFactory.CreateValidMessage();
}
```

### 5.2 测试性能优化

#### 5.2.1 并行测试执行
```csharp
// 启用并行测试
[assembly: CollectionBehavior(DisableTestParallelization = false)]

// 设置并行度
[assembly: Parallelizable(ParallelScope.All)]
```

#### 5.2.2 测试数据共享优化
```csharp
// 使用共享测试数据
public class SharedTestData : IClassFixture<SharedTestDataFixture>
{
    private readonly SharedTestDataFixture _fixture;
    
    public SharedTestData(SharedTestDataFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public void Test1()
    {
        var message = _fixture.GetTestMessage();
        // 使用共享测试数据
    }
}
```

这个文档提供了全面的测试实施建议和最佳实践，帮助你建立一个高质量、可维护的测试体系。通过遵循这些指导原则，你可以确保TelegramSearchBot项目的测试质量和开发效率。