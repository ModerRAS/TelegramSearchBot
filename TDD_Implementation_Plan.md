# TelegramSearchBot TDD实施方案

## 1. 项目结构分析

### 核心领域模型识别
基于代码分析，TelegramSearchBot的核心领域模型包括：

**核心领域：**
- **Message领域**：消息的存储、检索、处理
- **Search领域**：全文搜索（Lucene.NET）和向量搜索（FAISS）
- **AI服务领域**：OCR、ASR、LLM服务
- **User/Group管理领域**：用户、群组、权限管理
- **Media处理领域**：图片、音频、视频处理
- **Bot通信领域**：Telegram API交互

**现有项目结构：**
```
TelegramSearchBot.sln
├── TelegramSearchBot/           # 主应用程序
├── TelegramSearchBot.Data/      # 数据模型和EF Core
├── TelegramSearchBot.AI/        # AI服务实现
├── TelegramSearchBot.Search/    # 搜索服务
├── TelegramSearchBot.Vector/    # 向量搜索
├── TelegramSearchBot.Media/      # 媒体处理
├── TelegramSearchBot.Common/    # 通用组件
├── TelegramSearchBot.Infrastructure/ # 基础设施
└── TelegramSearchBot.Test/      # 现有测试项目
```

## 2. TDD实施方案设计

### 2.1 测试项目重新组织

**建议的测试项目结构：**
```
TelegramSearchBot.Tests/
├── TelegramSearchBot.Domain.Tests/           # 领域模型测试
│   ├── Message/
│   │   ├── MessageEntityTests.cs
│   │   ├── MessageServiceTests.cs
│   │   └── MessageRepositoryTests.cs
│   ├── Search/
│   │   ├── SearchServiceTests.cs
│   │   └── SearchQueryTests.cs
│   └── UserGroup/
│       ├── UserEntityTests.cs
│       └── GroupManagementTests.cs
├── TelegramSearchBot.Application.Tests/      # 应用服务测试
│   ├── AI/
│   │   ├── OCRServiceTests.cs
│   │   ├── ASRServiceTests.cs
│   │   └── LLMServiceTests.cs
│   ├── Media/
│   │   ├── MediaProcessingTests.cs
│   │   └── MediaStorageTests.cs
│   └── Bot/
│       ├── BotCommandTests.cs
│       └── BotUpdateHandlerTests.cs
├── TelegramSearchBot.Infrastructure.Tests/  # 基础设施测试
│   ├── Database/
│   │   ├── DbContextTests.cs
│   │   └── RepositoryTests.cs
│   ├── Search/
│   │   ├── LuceneTests.cs
│   │   └── FaissTests.cs
│   └── External/
│       ├── TelegramApiTests.cs
│       └── RedisCacheTests.cs
├── TelegramSearchBot.Integration.Tests/      # 集成测试
│   ├── MessageProcessingIntegrationTests.cs
│   ├── SearchIntegrationTests.cs
│   └── AIIntegrationTests.cs
└── TelegramSearchBot.Acceptance.Tests/       # 验收测试
    ├── EndToEndTests.cs
    └── UserScenarioTests.cs
```

### 2.2 测试命名规范

**单元测试命名规范：**
```
[UnitOfWork_StateUnderTest_ExpectedBehavior]

示例：
- MessageService_AddNewMessage_ShouldStoreInDatabase
- SearchService_SearchByKeyword_ShouldReturnRelevantResults
- OCRService_ExtractTextFromImage_ShouldReturnAccurateText
```

**集成测试命名规范：**
```
[Feature_IntegrationScenario_ExpectedOutcome]

示例：
- MessageProcessing_EndToEnd_ShouldProcessAndStoreMessage
- AIIntegration_OCRAndSearch_ShouldExtractAndIndexText
```

**测试类命名规范：**
```
[EntityName]Tests
[ServiceName]Tests
[FeatureName]Tests

示例：
- MessageEntityTests
- MessageServiceTests
- OCRProcessingTests
```

### 2.3 Mock策略

**Mock框架：Moq**

**Mock原则：**
1. **只Mock接口**：避免Mock具体类
2. **不要Mock静态类**：重构为可注入的依赖
3. **不要Mock值对象**：直接使用真实实例
4. **Mock外部依赖**：数据库、API、文件系统

**Mock策略示例：**
```csharp
// 数据库Mock
var mockDbContext = new Mock<DataDbContext>();
var mockDbSet = new Mock<DbSet<Message>>();
mockDbContext.Setup(ctx => ctx.Messages).Returns(mockDbSet.Object);

// 服务Mock
var mockTelegramBotClient = new Mock<ITelegramBotClient>();
var mockLLMService = new Mock<ILLMService>();

// 外部API Mock
var mockHttpClient = new Mock<HttpClient>();
mockHttpClient.Setup(client => client.GetAsync(It.IsAny<string>()))
    .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
```

### 2.4 测试数据管理

**测试数据工厂模式：**
```csharp
public class MessageTestDataFactory
{
    public static MessageOption CreateValidMessageOption(
        long userId = 1L,
        long chatId = 100L,
        string content = "Test message")
    {
        return new MessageOption
        {
            UserId = userId,
            User = new User { Id = userId, FirstName = "Test", Username = "testuser" },
            ChatId = chatId,
            Chat = new Chat { Id = chatId, Title = "Test Chat" },
            Content = content,
            DateTime = DateTime.UtcNow,
            MessageId = 1000
        };
    }

    public static Message CreateValidMessage(
        long groupId = 100L,
        long messageId = 1000L,
        string content = "Test message")
    {
        return new Message
        {
            GroupId = groupId,
            MessageId = messageId,
            FromUserId = 1L,
            Content = content,
            DateTime = DateTime.UtcNow
        };
    }
}
```

**测试数据Builders：**
```csharp
public class MessageBuilder
{
    private Message _message = new Message();
    
    public MessageBuilder WithGroupId(long groupId)
    {
        _message.GroupId = groupId;
        return this;
    }
    
    public MessageBuilder WithContent(string content)
    {
        _message.Content = content;
        return this;
    }
    
    public Message Build() => _message;
}
```

## 3. AAA模式实施

### 3.1 标准AAA结构
```csharp
[Fact]
public async Task MessageService_AddMessage_ShouldStoreInDatabase()
{
    // Arrange - 准备测试数据和依赖
    var mockDbContext = CreateMockDbContext();
    var mockLogger = new Mock<ILogger<MessageService>>();
    var service = new MessageService(mockDbContext.Object, mockLogger.Object);
    var messageOption = MessageTestDataFactory.CreateValidMessageOption();
    
    // Act - 执行要测试的操作
    var result = await service.AddMessageAsync(messageOption);
    
    // Assert - 验证结果
    Assert.True(result > 0);
    mockDbContext.Verify(ctx => ctx.Messages.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Once);
    mockDbContext.Verify(ctx => ctx.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
}
```

### 3.2 自定义Assert扩展
```csharp
public static class MessageAssertExtensions
{
    public static void ShouldBeValidMessage(this Message message)
    {
        Assert.NotNull(message);
        Assert.True(message.MessageId > 0);
        Assert.True(message.GroupId > 0);
        Assert.NotEmpty(message.Content);
        Assert.True(message.FromUserId > 0);
    }
    
    public static void ShouldContainMessage(this IEnumerable<Message> messages, string expectedContent)
    {
        Assert.Contains(messages, m => m.Content.Contains(expectedContent));
    }
}
```

## 4. TDD工作流程

### 4.1 Red-Green-Refactor循环

**Red阶段（写失败的测试）：**
1. 理解需求
2. 编写测试用例，确保测试失败
3. 验证测试确实失败（显示红色）

**Green阶段（使测试通过）：**
1. 编写最简单的代码使测试通过
2. 不要过度设计
3. 只关注让测试通过

**Refactor阶段（重构）：**
1. 消除重复代码
2. 改善设计
3. 确保所有测试仍然通过

### 4.2 示例：Message服务TDD

**Step 1: Red - 写失败的测试**
```csharp
[Fact]
public async Task MessageService_GetMessageById_ShouldReturnMessage()
{
    // Arrange
    var mockDbContext = new Mock<DataDbContext>();
    var mockLogger = new Mock<ILogger<MessageService>>();
    var service = new MessageService(mockDbContext.Object, mockLogger.Object);
    
    // Act
    var result = await service.GetMessageByIdAsync(1000, 100);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal(1000, result.MessageId);
    Assert.Equal(100, result.GroupId);
}
```

**Step 2: Green - 实现功能**
```csharp
public async Task<Message> GetMessageByIdAsync(long messageId, long groupId)
{
    return await _dbContext.Messages
        .FirstOrDefaultAsync(m => m.MessageId == messageId && m.GroupId == groupId);
}
```

**Step 3: Refactor - 重构优化**
```csharp
public async Task<Message?> GetMessageByIdAsync(long messageId, long groupId)
{
    return await _dbContext.Messages
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.MessageId == messageId && m.GroupId == groupId);
}
```

## 5. 测试配置和工具

### 5.1 测试项目配置
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.3" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.7" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>
</Project>
```

### 5.2 测试基类设计
```csharp
public abstract class TestBase
{
    protected Mock<ILogger<T>> CreateLoggerMock<T>() where T : class
    {
        return new Mock<ILogger<T>>();
    }
    
    protected Mock<DataDbContext> CreateMockDbContext()
    {
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new Mock<DataDbContext>(options);
    }
    
    protected Mock<ITelegramBotClient> CreateMockBotClient()
    {
        return new Mock<ITelegramBotClient>();
    }
}

public abstract class MessageServiceTestBase : TestBase
{
    protected MessageService CreateService(
        DataDbContext? dbContext = null,
        ILogger<MessageService>? logger = null,
        IMediator? mediator = null)
    {
        return new MessageService(
            logger ?? CreateLoggerMock<MessageService>().Object,
            dbContext ?? CreateMockDbContext().Object,
            mediator ?? Mock.Of<IMediator>());
    }
}
```

## 6. 持续集成配置

### 6.1 GitHub Actions配置
```yaml
name: CI/CD Pipeline

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
    
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
    
    - name: Build
      run: dotnet build TelegramSearchBot.sln --configuration Release
    
    - name: Run unit tests
      run: dotnet test TelegramSearchBot.sln --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory=Unit"
    
    - name: Run integration tests
      run: dotnet test TelegramSearchBot.sln --configuration Release --filter "TestCategory=Integration"
    
    - name: Generate coverage report
      run: |
        dotnet tool install -g dotnet-reportgenerator-globaltool
        reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:Html
    
    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v3
      with:
        file: ./coverage.xml
```

### 6.2 测试覆盖率目标
- **单元测试覆盖率**: ≥80%
- **集成测试覆盖率**: ≥60%
- **关键业务逻辑**: ≥95%

## 7. 测试最佳实践

### 7.1 单元测试最佳实践
1. **测试应该快速运行**（<1秒）
2. **测试应该是独立的**，不依赖执行顺序
3. **测试应该是可重复的**，不依赖外部状态
4. **测试应该有明确的Arrange-Act-Assert结构**
5. **避免测试实现细节**，专注业务逻辑

### 7.2 集成测试最佳实践
1. **使用真实的数据库**（InMemory或TestContainer）
2. **Mock外部API调用**
3. **测试完整的工作流程**
4. **验证跨组件交互**

### 7.3 测试反模式
1. **不要测试私有方法**
2. **不要测试第三方库**
3. **不要在测试中有条件逻辑**
4. **不要在测试中捕获异常并继续**

## 8. 实施建议

### 8.1 实施步骤
1. **第1周**：建立测试基础设施，创建测试项目结构
2. **第2-3周**：为Message领域编写单元测试
3. **第4-5周**：为Search领域编写单元测试
4. **第6-7周**：为AI服务编写单元测试
5. **第8周**：集成测试和端到端测试

### 8.2 团队培训
- TDD理念和最佳实践培训
- xUnit和Moq框架培训
- 代码审查和测试质量保证

### 8.3 质量保证
- 所有新功能必须先写测试
- 代码审查必须包含测试审查
- 测试覆盖率作为CI/CD的门槛
- 定期重构和优化测试代码

这个TDD实施方案为TelegramSearchBot项目提供了完整的测试驱动开发指导，确保代码质量和可维护性。