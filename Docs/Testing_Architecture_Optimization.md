# TelegramSearchBot 测试架构优化方案

## 1. 当前测试项目结构问题分析

### 1.1 现有问题
- **结构混乱**：Domain、Service、Data等层级混杂，不符合DDD原则
- **职责不清**：测试类型不明确，单元测试与集成测试混合
- **重复代码**：多个测试类中有重复的测试数据和Mock设置
- **维护困难**：缺乏统一的测试基类和工具类

### 1.2 优化目标
- **清晰的分层结构**：按照DDD领域模型组织测试
- **类型明确**：区分单元测试、集成测试、端到端测试
- **高复用性**：统一的测试基类、数据工厂、Mock工具
- **易于维护**：标准化的测试模式和命名规范

## 2. 优化后的测试项目结构

```
TelegramSearchBot.Test/
├── Unit/                          # 单元测试 (70%)
│   ├── Domain/                    # 领域层测试
│   │   ├── Model/                 # 实体测试
│   │   │   ├── Message/
│   │   │   ├── User/
│   │   │   ├── Group/
│   │   │   └── Search/
│   │   ├── Service/               # 领域服务测试
│   │   │   ├── MessageProcessing/
│   │   │   ├── Search/
│   │   │   └── AI/
│   │   └── Specification/          # 规约模式测试
│   ├── Application/               # 应用层测试
│   │   ├── Command/               # 命令处理器测试
│   │   ├── Query/                 # 查询处理器测试
│   │   ├── Notification/          # 通知处理器测试
│   │   └── Service/               # 应用服务测试
│   └── Infrastructure/            # 基础设施层测试
│       ├── Data/                  # 数据访问测试
│       ├── External/              # 外部服务测试
│       └── Common/                # 通用组件测试
├── Integration/                   # 集成测试 (20%)
│   ├── Database/                  # 数据库集成测试
│   ├── Services/                  # 服务集成测试
│   ├── MessagePipeline/           # 消息管道集成测试
│   └── ExternalAPI/               # 外部API集成测试
├── EndToEnd/                      # 端到端测试 (10%)
│   ├── MessageProcessing/         # 消息处理流程测试
│   ├── SearchWorkflow/            # 搜索工作流测试
│   └── AIProcessing/              # AI处理流程测试
├── Performance/                   # 性能测试
│   ├── Search/                    # 搜索性能测试
│   ├── AIProcessing/              # AI处理性能测试
│   └── Database/                  # 数据库性能测试
├── Security/                      # 安全测试
│   ├── InputValidation/           # 输入验证测试
│   ├── Authorization/             # 权限验证测试
│   └── DataProtection/            # 数据保护测试
├── Common/                        # 通用测试工具
│   ├── TestBase/                  # 测试基类
│   ├── TestData/                  # 测试数据工厂
│   ├── Mocks/                     # Mock对象工厂
│   ├── Assertions/                # 自定义断言
│   └── Extensions/                # 测试扩展方法
└── TestData/                      # 测试数据文件
    ├── Json/
    ├── Images/
    ├── Audio/
    └── Video/
```

## 3. 核心测试基类设计

### 3.1 通用测试基类
```csharp
// Common/TestBase/UnitTestBase.cs
namespace TelegramSearchBot.Test.Common.TestBase
{
    public abstract class UnitTestBase : IDisposable
    {
        protected readonly MockRepository MockRepository;
        protected readonly ITestOutputHelper Output;
        
        protected UnitTestBase(ITestOutputHelper output)
        {
            Output = output;
            MockRepository = new MockRepository(MockBehavior.Strict);
        }
        
        public virtual void Dispose()
        {
            MockRepository.VerifyAll();
        }
        
        protected Mock<T> CreateMock<T>() where T : class
        {
            return MockRepository.Create<T>();
        }
        
        protected Mock<T> CreateLooseMock<T>() where T : class
        {
            return new Mock<T>(MockBehavior.Loose);
        }
    }
}
```

### 3.2 领域测试基类
```csharp
// Common/TestBase/DomainTestBase.cs
namespace TelegramSearchBot.Test.Common.TestBase
{
    public abstract class DomainTestBase : UnitTestBase
    {
        protected DomainTestBase(ITestOutputHelper output) : base(output)
        {
        }
        
        protected static void AssertDomainRuleBroken<TException>(Action action, string expectedMessage)
            where TException : Exception
        {
            var exception = Assert.Throws<TException>(action);
            Assert.Contains(expectedMessage, exception.Message);
        }
        
        protected static void AssertDomainEventPublished<TEvent>(IEnumerable<IDomainEvent> events, int expectedCount = 1)
        {
            var domainEvents = events.OfType<TEvent>().ToList();
            Assert.Equal(expectedCount, domainEvents.Count);
        }
    }
}
```

### 3.3 集成测试基类
```csharp
// Common/TestBase/IntegrationTestBase.cs
namespace TelegramSearchBot.Test.Common.TestBase
{
    public abstract class IntegrationTestBase : IClassFixture<TestDatabaseFixture>, IDisposable
    {
        protected readonly TestDatabaseFixture DatabaseFixture;
        protected readonly ITestOutputHelper Output;
        protected readonly IServiceProvider ServiceProvider;
        
        protected IntegrationTestBase(TestDatabaseFixture databaseFixture, ITestOutputHelper output)
        {
            DatabaseFixture = databaseFixture;
            Output = output;
            ServiceProvider = CreateServiceProvider();
        }
        
        protected virtual IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            
            // 注册测试服务
            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseFixture.DatabaseName));
                
            services.AddScoped<IMessageService, MessageService>();
            services.AddScoped<ISearchService, SearchService>();
            
            // 注册Mock服务
            services.AddSingleton(CreateMock<ILogger<MessageService>>().Object);
            services.AddSingleton(CreateMock<LuceneManager>().Object);
            
            return services.BuildServiceProvider();
        }
        
        protected T GetService<T>() where T : notnull
        {
            return ServiceProvider.GetRequiredService<T>();
        }
        
        protected async Task ClearDatabaseAsync()
        {
            await using var context = GetService<DataDbContext>();
            context.Messages.RemoveRange(context.Messages);
            context.MessageExtensions.RemoveRange(context.MessageExtensions);
            await context.SaveChangesAsync();
        }
        
        public virtual void Dispose()
        {
            ServiceProvider?.Dispose();
        }
    }
}
```

### 3.4 数据库测试固件
```csharp
// Common/TestBase/TestDatabaseFixture.cs
namespace TelegramSearchBot.Test.Common.TestBase
{
    public class TestDatabaseFixture : IDisposable
    {
        public string DatabaseName { get; } = Guid.NewGuid().ToString();
        private readonly DataDbContext _context;
        
        public TestDatabaseFixture()
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(DatabaseName)
                .Options;
                
            _context = new DataDbContext(options);
            _context.Database.EnsureCreated();
        }
        
        public async Task SeedTestDataAsync()
        {
            var messages = MessageTestDataFactory.CreateTestMessages(100);
            _context.Messages.AddRange(messages);
            await _context.SaveChangesAsync();
        }
        
        public async Task ClearDatabaseAsync()
        {
            _context.Messages.RemoveRange(_context.Messages);
            _context.MessageExtensions.RemoveRange(_context.MessageExtensions);
            await _context.SaveChangesAsync();
        }
        
        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
```

## 4. 测试数据工厂设计

### 4.1 Message测试数据工厂
```csharp
// Common/TestData/MessageTestDataFactory.cs
namespace TelegramSearchBot.Test.Common.TestData
{
    public static class MessageTestDataFactory
    {
        public static Message CreateValidMessage(Action<Message>? configure = null)
        {
            var message = new Message
            {
                Id = 1,
                GroupId = 100,
                MessageId = 1000,
                FromUserId = 1,
                Content = "Test message content",
                DateTime = DateTime.UtcNow,
                ReplyToUserId = 0,
                ReplyToMessageId = 0,
                MessageExtensions = new List<MessageExtension>()
            };
            
            configure?.Invoke(message);
            return message;
        }
        
        public static Message CreateMessageWithReply(Action<Message>? configure = null)
        {
            return CreateValidMessage(m =>
            {
                m.ReplyToUserId = 2;
                m.ReplyToMessageId = 999;
                configure?.Invoke(m);
            });
        }
        
        public static Message CreateMessageWithExtensions(Action<Message>? configure = null)
        {
            return CreateValidMessage(m =>
            {
                m.MessageExtensions = new List<MessageExtension>
                {
                    new MessageExtension
                    {
                        ExtensionType = "OCR",
                        ExtensionData = "Extracted text from image"
                    },
                    new MessageExtension
                    {
                        ExtensionType = "Vector",
                        ExtensionData = "0.1,0.2,0.3,0.4"
                    }
                };
                configure?.Invoke(m);
            });
        }
        
        public static List<Message> CreateMessageList(int count, Action<Message>? configure = null)
        {
            return Enumerable.Range(1, count)
                .Select(i => CreateValidMessage(m =>
                {
                    m.Id = i;
                    m.MessageId = 1000 + i;
                    m.Content = $"Test message {i}";
                    configure?.Invoke(m);
                }))
                .ToList();
        }
        
        public static Telegram.Bot.Types.Message CreateTelegramMessage(Action<Telegram.Bot.Types.Message>? configure = null)
        {
            var message = new Telegram.Bot.Types.Message
            {
                MessageId = 1000,
                Chat = new Chat { Id = 100 },
                From = new User { Id = 1, Username = "testuser" },
                Text = "Hello from Telegram",
                Date = DateTime.UtcNow
            };
            
            configure?.Invoke(message);
            return message;
        }
        
        public static Telegram.Bot.Types.Message CreateTelegramPhotoMessage(Action<Telegram.Bot.Types.Message>? configure = null)
        {
            var message = new Telegram.Bot.Types.Message
            {
                MessageId = 1001,
                Chat = new Chat { Id = 100 },
                From = new User { Id = 1, Username = "testuser" },
                Caption = "Test photo caption",
                Date = DateTime.UtcNow,
                Photo = new List<PhotoSize>
                {
                    new PhotoSize
                    {
                        FileId = "test_file_id",
                        FileUniqueId = "test_unique_id",
                        Width = 1280,
                        Height = 720,
                        FileSize = 102400
                    }
                }
            };
            
            configure?.Invoke(message);
            return message;
        }
    }
}
```

### 4.2 User测试数据工厂
```csharp
// Common/TestData/UserTestDataFactory.cs
namespace TelegramSearchBot.Test.Common.TestData
{
    public static class UserTestDataFactory
    {
        public static UserData CreateValidUser(Action<UserData>? configure = null)
        {
            var user = new UserData
            {
                Id = 1,
                UserId = 1001,
                GroupId = 100,
                Username = "testuser",
                FirstName = "Test",
                LastName = "User",
                IsBot = false,
                JoinDate = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                MessageCount = 10
            };
            
            configure?.Invoke(user);
            return user;
        }
        
        public static List<UserData> CreateUserList(int count, Action<UserData>? configure = null)
        {
            return Enumerable.Range(1, count)
                .Select(i => CreateValidUser(u =>
                {
                    u.Id = i;
                    u.UserId = 1000 + i;
                    u.Username = $"testuser{i}";
                    configure?.Invoke(u);
                }))
                .ToList();
        }
    }
}
```

### 4.3 Group测试数据工厂
```csharp
// Common/TestData/GroupTestDataFactory.cs
namespace TelegramSearchBot.Test.Common.TestData
{
    public static class GroupTestDataFactory
    {
        public static GroupData CreateValidGroup(Action<GroupData>? configure = null)
        {
            var group = new GroupData
            {
                Id = 1,
                GroupId = 100,
                GroupName = "Test Group",
                GroupType = "group",
                MemberCount = 50,
                CreatedDate = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                IsActive = true,
                Settings = new GroupSettings
                {
                    EnableSearch = true,
                    EnableAI = true,
                    EnableOCR = true,
                    EnableASR = true,
                    MaxMessageLength = 4096
                }
            };
            
            configure?.Invoke(group);
            return group;
        }
    }
}
```

## 5. Mock对象工厂设计

### 5.1 通用Mock工厂
```csharp
// Common/Mocks/MockFactory.cs
namespace TelegramSearchBot.Test.Common.Mocks
{
    public static class MockFactory
    {
        public static Mock<ILogger<T>> CreateLoggerMock<T>() where T : class
        {
            var mock = new Mock<ILogger<T>>();
            
            mock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Returns(true);
                
            return mock;
        }
        
        public static Mock<ITelegramBotClient> CreateTelegramBotClientMock()
        {
            var mock = new Mock<ITelegramBotClient>();
            
            mock.Setup(x => x.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Telegram.Bot.Types.Message { MessageId = 1 });
                
            mock.Setup(x => x.SendPhotoAsync(
                It.IsAny<ChatId>(),
                It.IsAny<InputFile>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Telegram.Bot.Types.Message { MessageId = 2 });
                
            mock.Setup(x => x.GetFileAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new File { FilePath = "test/path.jpg" });
                
            return mock;
        }
        
        public static Mock<IPaddleOCRService> CreateOCRServiceMock()
        {
            var mock = new Mock<IPaddleOCRService>();
            
            mock.Setup(x => x.ProcessImageAsync(It.IsAny<string>()))
                .ReturnsAsync("Extracted text from image");
                
            return mock;
        }
        
        public static Mock<IGeneralLLMService> CreateLLMServiceMock()
        {
            var mock = new Mock<IGeneralLLMService>();
            
            mock.Setup(x => x.GenerateResponseAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync("AI generated response");
                
            mock.Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });
                
            return mock;
        }
        
        public static Mock<IAutoASRService> CreateASRServiceMock()
        {
            var mock = new Mock<IAutoASRService>();
            
            mock.Setup(x => x.ProcessAudioAsync(It.IsAny<string>()))
                .ReturnsAsync("Transcribed audio text");
                
            return mock;
        }
        
        public static Mock<LuceneManager> CreateLuceneManagerMock()
        {
            var mock = new Mock<LuceneManager>(Mock.Of<SendMessage>());
            
            mock.Setup(x => x.IndexMessageAsync(It.IsAny<Message>()))
                .Returns(Task.CompletedTask);
                
            mock.Setup(x => x.SearchAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<SearchResult>());
                
            return mock;
        }
    }
}
```

## 6. 自定义断言设计

### 6.1 Message断言扩展
```csharp
// Common/Assertions/MessageAssertions.cs
namespace TelegramSearchBot.Test.Common.Assertions
{
    public static class MessageAssertions
    {
        public static void ShouldBeValidMessage(this Message message)
        {
            message.Should().NotBeNull();
            message.GroupId.Should().BeGreaterThan(0);
            message.MessageId.Should().BeGreaterThan(0);
            message.FromUserId.Should().BeGreaterThan(0);
            message.DateTime.Should().BeAfter(DateTime.MinValue);
            message.MessageExtensions.Should().NotBeNull();
        }
        
        public static void ShouldHaveExtension(this Message message, string extensionType)
        {
            message.MessageExtensions.Should().Contain(x => x.ExtensionType == extensionType);
        }
        
        public static void ShouldBeReplyTo(this Message message, int replyToUserId, int replyToMessageId)
        {
            message.ReplyToUserId.Should().Be(replyToUserId);
            message.ReplyToMessageId.Should().Be(replyToMessageId);
        }
        
        public static void ShouldBeFromUser(this Message message, int userId)
        {
            message.FromUserId.Should().Be(userId);
        }
        
        public static void ShouldBeInGroup(this Message message, int groupId)
        {
            message.GroupId.Should().Be(groupId);
        }
        
        public static void ShouldContainText(this Message message, string text)
        {
            message.Content.Should().Contain(text);
        }
    }
}
```

### 6.2 测试结果断言
```csharp
// Common/Assertions/SearchAssertions.cs
namespace TelegramSearchBot.Test.Common.Assertions
{
    public static class SearchAssertions
    {
        public static void ShouldHaveValidResults(this IEnumerable<SearchResult> results)
        {
            results.Should().NotBeNull();
            results.Should().BeInDescendingOrder(x => x.Score);
            results.Should().OnlyContain(x => x.Message != null);
            results.Should().OnlyContain(x => x.Score > 0);
        }
        
        public static void ShouldContainMessage(this IEnumerable<SearchResult> results, string expectedText)
        {
            results.Should().Contain(x => x.Message.Content.Contains(expectedText));
        }
        
        public static void ShouldHaveMinimumScore(this IEnumerable<SearchResult> results, float minimumScore)
        {
            results.Should().OnlyContain(x => x.Score >= minimumScore);
        }
    }
}
```

## 7. 测试扩展方法

### 7.1 数据库扩展
```csharp
// Common/Extensions/DatabaseExtensions.cs
namespace TelegramSearchBot.Test.Common.Extensions
{
    public static class DatabaseExtensions
    {
        public static async Task<Message> AddTestMessageAsync(this DataDbContext context, Action<Message>? configure = null)
        {
            var message = MessageTestDataFactory.CreateValidMessage(configure);
            context.Messages.Add(message);
            await context.SaveChangesAsync();
            return message;
        }
        
        public static async Task<List<Message>> AddTestMessagesAsync(this DataDbContext context, int count)
        {
            var messages = MessageTestDataFactory.CreateMessageList(count);
            context.Messages.AddRange(messages);
            await context.SaveChangesAsync();
            return messages;
        }
        
        public static async Task<UserData> AddTestUserAsync(this DataDbContext context, Action<UserData>? configure = null)
        {
            var user = UserTestDataFactory.CreateValidUser(configure);
            context.UserData.Add(user);
            await context.SaveChangesAsync();
            return user;
        }
        
        public static async Task<GroupData> AddTestGroupAsync(this DataDbContext context, Action<GroupData>? configure = null)
        {
            var group = GroupTestDataFactory.CreateValidGroup(configure);
            context.GroupData.Add(group);
            await context.SaveChangesAsync();
            return group;
        }
        
        public static async Task ClearAllDataAsync(this DataDbContext context)
        {
            context.MessageExtensions.RemoveRange(context.MessageExtensions);
            context.Messages.RemoveRange(context.Messages);
            context.UserData.RemoveRange(context.UserData);
            context.GroupData.RemoveRange(context.GroupData);
            await context.SaveChangesAsync();
        }
    }
}
```

### 7.2 Mock扩展
```csharp
// Common/Extensions/MockExtensions.cs
namespace TelegramSearchBot.Test.Common.Extensions
{
    public static class MockExtensions
    {
        public static void SetupSuccess<T>(this Mock<T> mock, Expression<Func<T, Task>> methodExpression)
            where T : class
        {
            mock.Setup(methodExpression).Returns(Task.CompletedTask);
        }
        
        public static void SetupSuccess<T, TResult>(this Mock<T> mock, Expression<Func<T, Task<TResult>>> methodExpression, TResult result)
            where T : class
        {
            mock.Setup(methodExpression).ReturnsAsync(result);
        }
        
        public static void SetupException<T, TException>(this Mock<T> mock, Expression<Func<T, Task>> methodExpression, string errorMessage)
            where T : class
            where TException : Exception, new()
        {
            mock.Setup(methodExpression).ThrowsAsync(new TException { Message = errorMessage });
        }
        
        public static void VerifyCalled<T>(this Mock<T> mock, Expression<Action<T>> methodExpression, Times times)
            where T : class
        {
            mock.Verify(methodExpression, times);
        }
        
        public static void VerifyCalled<T>(this Mock<T> mock, Expression<Action<T>> methodExpression)
            where T : class
        {
            mock.Verify(methodExpression, Times.Once);
        }
        
        public static void VerifyNotCalled<T>(this Mock<T> mock, Expression<Action<T>> methodExpression)
            where T : class
        {
            mock.Verify(methodExpression, Times.Never);
        }
    }
}
```

## 8. 测试配置文件

### 8.1 测试配置
```json
// TestConfiguration/testsettings.json
{
  "TestSettings": {
    "Database": {
      "UseInMemory": true,
      "SeedTestData": true,
      "TestDataCount": 100
    },
    "ExternalServices": {
      "UseMocks": true,
      "MockLatency": 0,
      "EnableFailureScenarios": false
    },
    "Performance": {
      "MaxTestDuration": "00:05:00",
      "WarningThreshold": "00:01:00",
      "EnableDetailedMetrics": true
    },
    "Logging": {
      "LogLevel": "Information",
      "EnableTestOutput": true,
      "LogToFile": false
    }
  }
}
```

### 8.2 覆盖率配置
```xml
<!-- Coverage.runsettings -->
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Output>coverage.xml</Output>
          <Exclude>
            <Function>.*\.Migrations\..*</Function>
            <Function>.*\.Test\..*</Function>
            <Function>.*\.Program</Function>
          </Exclude>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

## 9. 实施计划

### 9.1 第一阶段：基础架构搭建
1. 创建测试基类和工具类
2. 建立测试数据工厂
3. 设置Mock对象工厂
4. 配置测试环境

### 9.2 第二阶段：单元测试迁移
1. 迁移现有测试到新结构
2. 补充缺失的单元测试
3. 建立测试覆盖率基线
4. 优化测试执行速度

### 9.3 第三阶段：集成测试完善
1. 设计集成测试用例
2. 实现数据库集成测试
3. 实现服务集成测试
4. 建立性能测试基准

### 9.4 第四阶段：端到端测试建设
1. 设计端到端测试场景
2. 实现关键业务流程测试
3. 建立持续集成流程
4. 完善测试报告体系

## 10. 质量保证措施

### 10.1 代码审查清单
- [ ] 测试命名是否符合规范
- [ ] 是否遵循AAA模式
- [ ] 是否有足够的断言
- [ ] 是否测试了边界条件
- [ ] 是否有重复代码
- [ ] Mock对象是否正确设置

### 10.2 测试质量指标
- **测试通过率**：100%
- **代码覆盖率**：≥ 80%
- **测试执行时间**：单元测试 < 5分钟，集成测试 < 15分钟
- **Flaky测试率**：< 1%

### 10.3 持续改进
- 定期重构测试代码
- 优化测试执行速度
- 更新测试工具和框架
- 分享测试最佳实践

这个优化方案提供了完整的测试架构重构计划，确保测试项目的可维护性、可扩展性和高质量。