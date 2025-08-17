# TelegramSearchBot Message领域测试计划

## 1. Message领域概述

### 1.1 核心职责
- **消息存储**：接收、验证、持久化Telegram消息
- **消息检索**：支持多种搜索方式（关键词、语义、向量）
- **消息处理**：AI服务集成（OCR、ASR、LLM）
- **消息扩展**：管理消息的额外数据和元信息

### 1.2 关键组件
- **Message实体**：核心领域模型
- **MessageService**：消息业务逻辑服务
- **MessageExtension**：消息扩展数据
- **MessageProcessing**：消息处理管道
- **MessageRepository**：消息数据访问

## 2. 测试覆盖范围

### 2.1 实体测试
- [x] Message构造函数测试
- [x] Message.FromTelegramMessage测试
- [x] Message属性验证测试
- [ ] Message扩展集合测试
- [ ] Message领域规则测试
- [ ] Message序列化测试

### 2.2 服务测试
- [x] MessageService基本功能测试
- [ ] MessageService消息处理测试
- [ ] MessageService消息存储测试
- [ ] MessageService消息检索测试
- [ ] MessageService异常处理测试
- [ ] MessageService性能测试

### 2.3 仓储测试
- [x] MessageRepository基本操作测试
- [ ] MessageRepository查询测试
- [ ] MessageRepository批量操作测试
- [ ] MessageRepository事务测试
- [ ] MessageRepository性能测试

### 2.4 处理管道测试
- [ ] 消息接收处理测试
- [ ] 消息验证测试
- [ ] 消息AI处理测试
- [ ] 消息索引测试
- [ ] 消息通知测试

## 3. 详细测试用例设计

### 3.1 Message实体测试

#### 3.1.1 构造函数测试
```csharp
[Unit/Domain/Model/Message/MessageConstructorTests.cs]
public class MessageConstructorTests : DomainTestBase
{
    [Fact]
    public void Constructor_Default_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var message = new Message();
        
        // Assert
        message.Id.Should().Be(0);
        message.GroupId.Should().Be(0);
        message.MessageId.Should().Be(0);
        message.FromUserId.Should().Be(0);
        message.ReplyToUserId.Should().Be(0);
        message.ReplyToMessageId.Should().Be(0);
        message.Content.Should().BeNull();
        message.DateTime.Should().Be(default);
        message.MessageExtensions.Should().BeEmpty();
    }
    
    [Fact]
    public void Constructor_WithParameters_ShouldInitializeCorrectly()
    {
        // Arrange
        var groupId = 100;
        var messageId = 1000;
        var fromUserId = 1;
        var content = "Test message";
        var dateTime = DateTime.UtcNow;
        
        // Act
        var message = new Message
        {
            GroupId = groupId,
            MessageId = messageId,
            FromUserId = fromUserId,
            Content = content,
            DateTime = dateTime
        };
        
        // Assert
        message.GroupId.Should().Be(groupId);
        message.MessageId.Should().Be(messageId);
        message.FromUserId.Should().Be(fromUserId);
        message.Content.Should().Be(content);
        message.DateTime.Should().Be(dateTime);
    }
}
```

#### 3.1.2 FromTelegramMessage测试
```csharp
[Unit/Domain/Model/Message/MessageFromTelegramTests.cs]
public class MessageFromTelegramTests : DomainTestBase
{
    [Theory]
    [InlineData("Hello World", null, "Hello World")]
    [InlineData(null, "Image caption", "Image caption")]
    [InlineData("Text message", "Caption", "Text message")]
    public void FromTelegramMessage_ContentSource_ShouldUseCorrectContent(string? text, string? caption, string expectedContent)
    {
        // Arrange
        var telegramMessage = MessageTestDataFactory.CreateTelegramMessage(m =>
        {
            m.Text = text;
            m.Caption = caption;
        });
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.Content.Should().Be(expectedContent);
    }
    
    [Fact]
    public void FromTelegramMessage_WithReplyTo_ShouldSetReplyToFields()
    {
        // Arrange
        var telegramMessage = MessageTestDataFactory.CreateTelegramMessage(m =>
        {
            m.ReplyToMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 999,
                From = new User { Id = 2 }
            };
        });
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.ReplyToMessageId.Should().Be(999);
        result.ReplyToUserId.Should().Be(2);
    }
    
    [Fact]
    public void FromTelegramMessage_MediaMessage_ShouldHandleCorrectly()
    {
        // Arrange
        var telegramMessage = MessageTestDataFactory.CreateTelegramPhotoMessage();
        
        // Act
        var result = Message.FromTelegramMessage(telegramMessage);
        
        // Assert
        result.GroupId.Should().Be(telegramMessage.Chat.Id);
        result.FromUserId.Should().Be(telegramMessage.From!.Id);
        result.Content.Should().Be(telegramMessage.Caption);
        result.MessageId.Should().Be(telegramMessage.MessageId);
    }
}
```

#### 3.1.3 领域规则测试
```csharp
[Unit/Domain/Model/Message/MessageDomainRuleTests.cs]
public class MessageDomainRuleTests : DomainTestBase
{
    [Fact]
    public void Message_Validate_ShouldThrowWhenGroupIdIsZero()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage(m => m.GroupId = 0);
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>()
            .WithMessage("*Group ID must be greater than 0*");
    }
    
    [Fact]
    public void Message_Validate_ShouldThrowWhenMessageIdIsZero()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage(m => m.MessageId = 0);
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>()
            .WithMessage("*Message ID must be greater than 0*");
    }
    
    [Fact]
    public void Message_Validate_ShouldThrowWhenContentIsEmpty()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage(m => m.Content = "");
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().Throw<DomainException>()
            .WithMessage("*Message content cannot be empty*");
    }
    
    [Fact]
    public void Message_Validate_ShouldPassWhenAllFieldsAreValid()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        
        // Act & Assert
        var action = () => message.Validate();
        action.Should().NotThrow();
    }
}
```

### 3.2 MessageService测试

#### 3.2.1 消息处理测试
```csharp
[Unit/Application/Service/Message/MessageServiceProcessTests.cs]
public class MessageServiceProcessTests : UnitTestBase
{
    private readonly Mock<IMessageRepository> _messageRepository;
    private readonly Mock<ILuceneManager> _luceneManager;
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<ILogger<MessageService>> _logger;
    private readonly MessageService _messageService;
    
    public MessageServiceProcessTests(ITestOutputHelper output) : base(output)
    {
        _messageRepository = CreateMock<IMessageRepository>();
        _luceneManager = CreateMock<ILuceneManager>();
        _mediator = CreateMock<IMediator>();
        _logger = CreateMock<ILogger<MessageService>>();
        
        _messageService = new MessageService(
            _logger.Object,
            _luceneManager.Object,
            null!, // SendMessage not needed for these tests
            null!, // DbContext not needed for these tests
            _mediator.Object);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_ValidMessage_ShouldProcessSuccessfully()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        _messageRepository.Setup(x => x.AddAsync(message)).Returns(Task.CompletedTask);
        _luceneManager.Setup(x => x.IndexMessageAsync(message)).Returns(Task.CompletedTask);
        _mediator.Setup(x => x.Publish(It.IsAny<TextMessageReceivedNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Act
        var result = await _messageService.ProcessMessageAsync(message);
        
        // Assert
        result.Should().BeTrue();
        _messageRepository.Verify(x => x.AddAsync(message), Times.Once);
        _luceneManager.Verify(x => x.IndexMessageAsync(message), Times.Once);
        _mediator.Verify(x => x.Publish(
            It.Is<TextMessageReceivedNotification>(n => n.Message == message),
            It.IsAny<CancellationToken>()), Times.Once);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_DuplicateMessage_ShouldReturnFalse()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        _messageRepository.Setup(x => x.ExistsAsync(message.GroupId, message.MessageId))
            .ReturnsAsync(true);
        
        // Act
        var result = await _messageService.ProcessMessageAsync(message);
        
        // Assert
        result.Should().BeFalse();
        _messageRepository.Verify(x => x.AddAsync(message), Times.Never);
        _luceneManager.Verify(x => x.IndexMessageAsync(message), Times.Never);
    }
    
    [Fact]
    public async Task ProcessMessageAsync_RepositoryThrowsException_ShouldThrowAndLog()
    {
        // Arrange
        var message = MessageTestDataFactory.CreateValidMessage();
        _messageRepository.Setup(x => x.AddAsync(message))
            .ThrowsAsync(new DatabaseException("Database error"));
        
        // Act & Assert
        var action = async () => await _messageService.ProcessMessageAsync(message);
        await action.Should().ThrowAsync<DatabaseException>();
        
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<DatabaseException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

#### 3.2.2 消息检索测试
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
    
    [Fact]
    public async Task SearchMessagesAsync_ValidQuery_ShouldReturnResults()
    {
        // Arrange
        var query = "test query";
        var groupId = 100;
        var expectedResults = new List<SearchResult>
        {
            new SearchResult { Message = MessageTestDataFactory.CreateValidMessage(), Score = 0.9f }
        };
        
        _luceneManager.Setup(x => x.SearchAsync(query))
            .ReturnsAsync(expectedResults);
        
        // Act
        var result = await _messageService.SearchMessagesAsync(query, groupId);
        
        // Assert
        result.Should().BeEquivalentTo(expectedResults);
        _luceneManager.Verify(x => x.SearchAsync(query), Times.Once);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task SearchMessagesAsync_InvalidQuery_ShouldThrowArgumentException(string? invalidQuery)
    {
        // Arrange & Act & Assert
        var action = async () => await _messageService.SearchMessagesAsync(invalidQuery!, 100);
        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Query cannot be empty or whitespace*");
    }
    
    [Fact]
    public async Task SearchMessagesAsync_EmptyResults_ShouldReturnEmptyList()
    {
        // Arrange
        var query = "nonexistent query";
        _luceneManager.Setup(x => x.SearchAsync(query))
            .ReturnsAsync(new List<SearchResult>());
        
        // Act
        var result = await _messageService.SearchMessagesAsync(query, 100);
        
        // Assert
        result.Should().BeEmpty();
    }
}
```

### 3.3 MessageRepository测试

#### 3.3.1 基本操作测试
```csharp
[Unit/Infrastructure/Data/MessageRepositoryBasicTests.cs]
public class MessageRepositoryBasicTests : IntegrationTestBase
{
    private readonly IMessageRepository _repository;
    
    public MessageRepositoryBasicTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _repository = GetService<IMessageRepository>();
    }
    
    [Fact]
    public async Task AddAsync_ValidMessage_ShouldPersistToDatabase()
    {
        // Arrange
        await ClearDatabaseAsync();
        var message = MessageTestDataFactory.CreateValidMessage();
        
        // Act
        await _repository.AddAsync(message);
        
        // Assert
        await using var context = GetService<DataDbContext>();
        var savedMessage = await context.Messages.FindAsync(message.Id);
        savedMessage.Should().NotBeNull();
        savedMessage!.Content.Should().Be(message.Content);
        savedMessage.GroupId.Should().Be(message.GroupId);
    }
    
    [Fact]
    public async Task GetByIdAsync_ExistingMessage_ShouldReturnMessage()
    {
        // Arrange
        await ClearDatabaseAsync();
        var expectedMessage = await DatabaseFixture.Context.AddTestMessageAsync();
        
        // Act
        var result = await _repository.GetByIdAsync(expectedMessage.Id);
        
        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(expectedMessage.Id);
        result.Content.Should().Be(expectedMessage.Content);
    }
    
    [Fact]
    public async Task GetByIdAsync_NonExistingMessage_ShouldReturnNull()
    {
        // Arrange
        await ClearDatabaseAsync();
        
        // Act
        var result = await _repository.GetByIdAsync(999);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Fact]
    public async Task ExistsAsync_ExistingMessage_ShouldReturnTrue()
    {
        // Arrange
        await ClearDatabaseAsync();
        var message = await DatabaseFixture.Context.AddTestMessageAsync();
        
        // Act
        var result = await _repository.ExistsAsync(message.GroupId, message.MessageId);
        
        // Assert
        result.Should().BeTrue();
    }
    
    [Fact]
    public async Task ExistsAsync_NonExistingMessage_ShouldReturnFalse()
    {
        // Arrange
        await ClearDatabaseAsync();
        
        // Act
        var result = await _repository.ExistsAsync(100, 999);
        
        // Assert
        result.Should().BeFalse();
    }
}
```

#### 3.3.2 查询测试
```csharp
[Unit/Infrastructure/Data/MessageRepositoryQueryTests.cs]
public class MessageRepositoryQueryTests : IntegrationTestBase
{
    private readonly IMessageRepository _repository;
    
    public MessageRepositoryQueryTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _repository = GetService<IMessageRepository>();
    }
    
    [Fact]
    public async Task GetMessagesByGroupIdAsync_ShouldReturnFilteredMessages()
    {
        // Arrange
        await ClearDatabaseAsync();
        var groupId = 100;
        var messages = await DatabaseFixture.Context.AddTestMessagesAsync(10);
        
        // Act
        var result = await _repository.GetMessagesByGroupIdAsync(groupId);
        
        // Assert
        result.Should().HaveCount(10);
        result.Should().OnlyContain(x => x.GroupId == groupId);
    }
    
    [Fact]
    public async Task GetMessagesByUserIdAsync_ShouldReturnFilteredMessages()
    {
        // Arrange
        await ClearDatabaseAsync();
        var userId = 1;
        var messages = await DatabaseFixture.Context.AddTestMessagesAsync(5, m => m.FromUserId = userId);
        
        // Act
        var result = await _repository.GetMessagesByUserIdAsync(userId);
        
        // Assert
        result.Should().HaveCount(5);
        result.Should().OnlyContain(x => x.FromUserId == userId);
    }
    
    [Fact]
    public async Task GetMessagesByDateRangeAsync_ShouldReturnFilteredMessages()
    {
        // Arrange
        await ClearDatabaseAsync();
        var startDate = DateTime.UtcNow.AddDays(-7);
        var endDate = DateTime.UtcNow;
        var messages = await DatabaseFixture.Context.AddTestMessagesAsync(3, m => 
        {
            m.DateTime = DateTime.UtcNow.AddDays(-1);
        });
        
        // Act
        var result = await _repository.GetMessagesByDateRangeAsync(startDate, endDate);
        
        // Assert
        result.Should().HaveCount(3);
        result.Should().OnlyContain(x => x.DateTime >= startDate && x.DateTime <= endDate);
    }
    
    [Fact]
    public async Task GetRecentMessagesAsync_ShouldReturnMostRecentMessages()
    {
        // Arrange
        await ClearDatabaseAsync();
        await DatabaseFixture.Context.AddTestMessagesAsync(20);
        
        // Act
        var result = await _repository.GetRecentMessagesAsync(100, 10);
        
        // Assert
        result.Should().HaveCount(10);
        result.Should().BeInDescendingOrder(x => x.DateTime);
    }
}
```

### 3.4 消息处理管道测试

#### 3.4.1 消息接收处理测试
```csharp
[Integration/MessagePipeline/MessageReceivingTests.cs]
public class MessageReceivingTests : IntegrationTestBase
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly Mock<ITelegramBotClient> _botClient;
    
    public MessageReceivingTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _messageProcessor = GetService<IMessageProcessor>();
        _botClient = MockFactory.CreateTelegramBotClientMock();
    }
    
    [Fact]
    public async Task ProcessTelegramUpdate_TextMessage_ShouldProcessSuccessfully()
    {
        // Arrange
        await ClearDatabaseAsync();
        var update = new Update
        {
            Id = 1,
            Message = MessageTestDataFactory.CreateTelegramMessage()
        };
        
        // Act
        var result = await _messageProcessor.ProcessUpdateAsync(update, _botClient.Object);
        
        // Assert
        result.Should().BeTrue();
        
        // Verify message was stored
        await using var context = GetService<DataDbContext>();
        var storedMessage = await context.Messages.FirstOrDefaultAsync();
        storedMessage.Should().NotBeNull();
        storedMessage!.Content.Should().Be(update.Message.Text);
    }
    
    [Fact]
    public async Task ProcessTelegramUpdate_PhotoMessage_ShouldProcessSuccessfully()
    {
        // Arrange
        await ClearDatabaseAsync();
        var update = new Update
        {
            Id = 2,
            Message = MessageTestDataFactory.CreateTelegramPhotoMessage()
        };
        
        // Act
        var result = await _messageProcessor.ProcessUpdateAsync(update, _botClient.Object);
        
        // Assert
        result.Should().BeTrue();
        
        // Verify message was stored with caption
        await using var context = GetService<DataDbContext>();
        var storedMessage = await context.Messages.FirstOrDefaultAsync();
        storedMessage.Should().NotBeNull();
        storedMessage!.Content.Should().Be(update.Message.Caption);
    }
    
    [Fact]
    public async Task ProcessTelegramUpdate_DuplicateMessage_ShouldNotProcess()
    {
        // Arrange
        await ClearDatabaseAsync();
        var telegramMessage = MessageTestDataFactory.CreateTelegramMessage();
        var update1 = new Update { Id = 1, Message = telegramMessage };
        var update2 = new Update { Id = 2, Message = telegramMessage };
        
        // Act
        var result1 = await _messageProcessor.ProcessUpdateAsync(update1, _botClient.Object);
        var result2 = await _messageProcessor.ProcessUpdateAsync(update2, _botClient.Object);
        
        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
        
        // Verify only one message was stored
        await using var context = GetService<DataDbContext>();
        var messageCount = await context.Messages.CountAsync();
        messageCount.Should().Be(1);
    }
}
```

#### 3.4.2 消息AI处理测试
```csharp
[Integration/MessagePipeline/MessageAIProcessingTests.cs]
public class MessageAIProcessingTests : IntegrationTestBase
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly Mock<IPaddleOCRService> _ocrService;
    private readonly Mock<IAutoASRService> _asrService;
    private readonly Mock<IGeneralLLMService> _llmService;
    
    public MessageAIProcessingTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _messageProcessor = GetService<IMessageProcessor>();
        _ocrService = CreateMock<IPaddleOCRService>();
        _asrService = CreateMock<IAutoASRService>();
        _llmService = CreateMock<IGeneralLLMService>();
        
        // Replace service registrations with mocks
        var services = new ServiceCollection();
        services.AddSingleton(_ocrService.Object);
        services.AddSingleton(_asrService.Object);
        services.AddSingleton(_llmService.Object);
        
        // Add other required services...
        ServiceProvider = services.BuildServiceProvider();
    }
    
    [Fact]
    public async Task ProcessMessageWithPhoto_ShouldTriggerOCR()
    {
        // Arrange
        await ClearDatabaseAsync();
        var message = MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.MessageExtensions.Add(new MessageExtension
            {
                ExtensionType = "Photo",
                ExtensionData = "photo_path.jpg"
            });
        });
        
        _ocrService.Setup(x => x.ProcessImageAsync("photo_path.jpg"))
            .ReturnsAsync("Extracted text from photo");
        
        // Act
        var result = await _messageProcessor.ProcessMessageAIExtensionsAsync(message);
        
        // Assert
        result.Should().BeTrue();
        message.ShouldHaveExtension("OCR");
        _ocrService.Verify(x => x.ProcessImageAsync("photo_path.jpg"), Times.Once);
    }
    
    [Fact]
    public async Task ProcessMessageWithAudio_ShouldTriggerASR()
    {
        // Arrange
        await ClearDatabaseAsync();
        var message = MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.MessageExtensions.Add(new MessageExtension
            {
                ExtensionType = "Audio",
                ExtensionData = "audio_path.mp3"
            });
        });
        
        _asrService.Setup(x => x.ProcessAudioAsync("audio_path.mp3"))
            .ReturnsAsync("Transcribed audio text");
        
        // Act
        var result = await _messageProcessor.ProcessMessageAIExtensionsAsync(message);
        
        // Assert
        result.Should().BeTrue();
        message.ShouldHaveExtension("ASR");
        _asrService.Verify(x => x.ProcessAudioAsync("audio_path.mp3"), Times.Once);
    }
    
    [Fact]
    public async Task ProcessMessageWithText_ShouldTriggerVectorGeneration()
    {
        // Arrange
        await ClearDatabaseAsync();
        var message = MessageTestDataFactory.CreateValidMessage();
        
        _llmService.Setup(x => x.GenerateEmbeddingAsync(message.Content))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });
        
        // Act
        var result = await _messageProcessor.ProcessMessageAIExtensionsAsync(message);
        
        // Assert
        result.Should().BeTrue();
        message.ShouldHaveExtension("Vector");
        _llmService.Verify(x => x.GenerateEmbeddingAsync(message.Content), Times.Once);
    }
}
```

## 4. 性能测试设计

### 4.1 消息存储性能测试
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
    
    [Fact]
    public async Task AddMessages_BulkInsert_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        await ClearDatabaseAsync();
        var messages = MessageTestDataFactory.CreateMessageList(1000);
        var stopwatch = new Stopwatch();
        
        // Act
        stopwatch.Start();
        foreach (var message in messages)
        {
            await _repository.AddAsync(message);
        }
        stopwatch.Stop();
        
        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // 5秒内完成
        Output.WriteLine($"Added {messages.Count} messages in {stopwatch.ElapsedMilliseconds}ms");
    }
    
    [Fact]
    public async Task GetMessages_LargeDataset_ShouldCompleteWithinTimeLimit()
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
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // 1秒内完成
        results.Should().HaveCount(10000);
        Output.WriteLine($"Retrieved {results.Count} messages in {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

### 4.2 消息搜索性能测试
```csharp
[Performance/Message/MessageSearchPerformanceTests.cs]
public class MessageSearchPerformanceTests : IntegrationTestBase
{
    private readonly IMessageService _messageService;
    
    public MessageSearchPerformanceTests(TestDatabaseFixture databaseFixture, ITestOutputHelper output) 
        : base(databaseFixture, output)
    {
        _messageService = GetService<IMessageService>();
    }
    
    [Fact]
    public async Task SearchMessages_LargeDataset_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        await ClearDatabaseAsync();
        await DatabaseFixture.Context.AddTestMessagesAsync(5000);
        var stopwatch = new Stopwatch();
        
        // Act
        stopwatch.Start();
        var results = await _messageService.SearchMessagesAsync("test query", 100);
        stopwatch.Stop();
        
        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // 500ms内完成
        Output.WriteLine($"Search completed in {stopwatch.ElapsedMilliseconds}ms, found {results.Count} results");
    }
    
    [Fact]
    public async Task SearchMessages_ConcurrentRequests_ShouldHandleLoad()
    {
        // Arrange
        await ClearDatabaseAsync();
        await DatabaseFixture.Context.AddTestMessagesAsync(1000);
        var tasks = new List<Task<List<SearchResult>>>();
        var stopwatch = new Stopwatch();
        
        // Act
        stopwatch.Start();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_messageService.SearchMessagesAsync($"test query {i}", 100));
        }
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000); // 2秒内完成所有请求
        tasks.Should().AllSatisfy(t => t.Result.Should().NotBeNull());
        Output.WriteLine($"Completed {tasks.Count} concurrent searches in {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

## 5. 测试数据管理

### 5.1 测试数据场景
```csharp
[Common/TestData/MessageTestScenarios.cs]
public static class MessageTestScenarios
{
    public static List<Message> CreateGroupChatScenario()
    {
        var messages = new List<Message>();
        var groupId = 100;
        
        // 创建群聊对话场景
        messages.Add(MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.Id = 1;
            m.MessageId = 1000;
            m.FromUserId = 1;
            m.Content = "大家好！欢迎加入群聊";
            m.GroupId = groupId;
        }));
        
        messages.Add(MessageTestDataFactory.CreateMessageWithReply(m =>
        {
            m.Id = 2;
            m.MessageId = 1001;
            m.FromUserId = 2;
            m.Content = "谢谢！很高兴加入";
            m.GroupId = groupId;
            m.ReplyToUserId = 1;
            m.ReplyToMessageId = 1000;
        }));
        
        messages.Add(MessageTestDataFactory.CreateValidMessage(m =>
        {
            m.Id = 3;
            m.MessageId = 1002;
            m.FromUserId = 3;
            m.Content = "有人知道这个问题吗？";
            m.GroupId = groupId;
        }));
        
        return messages;
    }
    
    public static List<Message> CreateMediaProcessingScenario()
    {
        var messages = new List<Message>();
        
        // 创建包含多种媒体的消息
        messages.Add(MessageTestDataFactory.CreateMessageWithExtensions(m =>
        {
            m.Id = 1;
            m.MessageId = 1000;
            m.Content = "查看这张图片";
            m.MessageExtensions = new List<MessageExtension>
            {
                new MessageExtension { ExtensionType = "Photo", ExtensionData = "image1.jpg" },
                new MessageExtension { ExtensionType = "OCR", ExtensionData = "图片中的文字" }
            };
        }));
        
        messages.Add(MessageTestDataFactory.CreateMessageWithExtensions(m =>
        {
            m.Id = 2;
            m.MessageId = 1001;
            m.Content = "听听这段录音";
            m.MessageExtensions = new List<MessageExtension>
            {
                new MessageExtension { ExtensionType = "Audio", ExtensionData = "audio1.mp3" },
                new MessageExtension { ExtensionType = "ASR", ExtensionData = "录音转写的文字" }
            };
        }));
        
        return messages;
    }
    
    public static List<Message> CreateSearchTestingScenario()
    {
        var messages = new List<Message>();
        var searchTerms = new[] { "算法", "数据结构", "编程", "开发", "测试" };
        
        for (int i = 0; i < 100; i++)
        {
            messages.Add(MessageTestDataFactory.CreateValidMessage(m =>
            {
                m.Id = i + 1;
                m.MessageId = 1000 + i;
                m.Content = $"讨论{searchTerms[i % searchTerms.Length]}相关的话题";
                m.FromUserId = (i % 10) + 1;
            }));
        }
        
        return messages;
    }
}
```

## 6. 测试执行计划

### 6.1 测试分类执行
```bash
# 执行Message领域所有测试
dotnet test --filter "Message"

# 执行Message实体测试
dotnet test --filter "MessageEntity"

# 执行Message服务测试
dotnet test --filter "MessageService"

# 执行Message仓储测试
dotnet test --filter "MessageRepository"

# 执行Message性能测试
dotnet test --filter "MessagePerformance"
```

### 6.2 测试覆盖率目标
- **Message实体**：100% 覆盖率
- **MessageService**：90% 覆盖率
- **MessageRepository**：85% 覆盖率
- **Message处理管道**：80% 覆盖率
- **整体Message领域**：85% 覆盖率

## 7. 持续集成集成

### 7.1 GitHub Actions配置
```yaml
name: Message Tests
on: [push, pull_request]
jobs:
  message-tests:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 9.0.x
    - name: Run Message unit tests
      run: dotnet test --filter "Category=Unit&Message" --collect:"XPlat Code Coverage"
    - name: Run Message integration tests
      run: dotnet test --filter "Category=Integration&Message" --collect:"XPlat Code Coverage"
    - name: Run Message performance tests
      run: dotnet test --filter "Category=Performance&Message"
    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v1
```

这个Message领域测试计划提供了全面的测试覆盖，从实体测试到性能测试，确保Message领域的功能正确性和性能要求。