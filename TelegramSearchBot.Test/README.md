# TelegramSearchBot 测试工具使用指南

本指南介绍了TelegramSearchBot项目中新增的测试工具类和辅助方法，旨在提高测试开发效率，减少重复代码，并提供一致的测试体验。

## 概述

我们创建了以下核心测试工具类：

1. **TestDatabaseHelper.cs** - 数据库测试辅助类
2. **MockServiceFactory.cs** - Mock对象工厂
3. **TestAssertionExtensions.cs** - 自定义断言扩展
4. **TestConfigurationHelper.cs** - 测试配置辅助类
5. **IntegrationTestBase.cs** - 集成测试基类

## 1. TestDatabaseHelper.cs - 数据库测试辅助类

### 主要功能

- **快速创建InMemory数据库**：`CreateInMemoryDbContext()`
- **事务支持**：`CreateInMemoryDbContextWithTransaction()`
- **批量数据操作**：`BulkInsertAsync()`, `ClearTableAsync()`
- **标准测试数据**：`CreateStandardTestDataAsync()`
- **数据库统计**：`GetDatabaseStatisticsAsync()`
- **数据库快照**：`CreateSnapshotAsync()`, `RestoreFromSnapshotAsync()`

### 使用示例

```csharp
[Fact]
public async Task Example_TestDatabaseHelper()
{
    // 创建InMemory数据库
    using var dbContext = TestDatabaseHelper.CreateInMemoryDbContext();
    
    // 创建标准测试数据
    var testData = await TestDatabaseHelper.CreateStandardTestDataAsync(dbContext);
    
    // 验证数据
    Assert.Equal(3, testData.Messages.Count);
    Assert.Equal(3, testData.Users.Count);
    
    // 获取统计信息
    var stats = await TestDatabaseHelper.GetDatabaseStatisticsAsync(dbContext);
    Assert.Equal(3, stats.MessageCount);
}
```

## 2. MockServiceFactory.cs - Mock对象工厂

### 主要功能

- **Telegram Bot Client Mock**：支持SendMessage、GetFile等操作
- **LLM Service Mock**：支持ChatCompletion、Embedding等AI操作
- **Logger Mock**：支持日志记录验证
- **HttpClient Mock**：支持HTTP请求模拟
- **Database Mock**：支持EF Core DbSet模拟

### 使用示例

```csharp
[Fact]
public void Example_MockServiceFactory()
{
    // 创建Telegram Bot Client Mock
    var botClientMock = MockServiceFactory.CreateTelegramBotClientWithSendMessage(
        "Hello, World!", 12345);
    
    // 创建LLM Service Mock
    var llmMock = MockServiceFactory.CreateLLMServiceWithChatCompletion(
        "AI response", TimeSpan.FromMilliseconds(100));
    
    // 创建Logger Mock
    var loggerMock = MockServiceFactory.CreateLoggerWithExpectedLog<MyService>(
        LogLevel.Information, "Expected log message");
    
    // 验证Mock配置
    Assert.NotNull(botClientMock);
    Assert.NotNull(llmMock);
    Assert.NotNull(loggerMock);
}
```

## 3. TestAssertionExtensions.cs - 自定义断言扩展

### 主要功能

- **消息验证**：`ShouldBeValidMessage()`, `ShouldBeReplyMessage()`
- **用户验证**：`ShouldBeValidUserData()`, `ShouldBePremiumUser()`
- **群组验证**：`ShouldBeValidGroupData()`, `ShouldBeForum()`
- **集合验证**：`ShouldContainMessageWithContent()`, `ShouldBeInChronologicalOrder()`
- **字符串验证**：`ShouldContainChinese()`, `ShouldContainEmoji()`
- **异步验证**：`ShouldCompleteWithinAsync()`, `ShouldThrowAsync<T>()`

### 使用示例

```csharp
[Fact]
public void Example_TestAssertionExtensions()
{
    var message = MessageTestDataFactory.CreateValidMessage();
    var user = MessageTestDataFactory.CreateUserData();
    
    // 使用自定义断言
    message.ShouldBeValidMessage(100, 1000, 1, "Test message");
    user.ShouldBeValidUserData("Test", "User", "testuser", false);
    
    // 验证特殊内容
    var specialText = "Hello 世界! 😊";
    specialText.ShouldContainChinese();
    specialText.ShouldContainEmoji();
    
    // 验证集合
    var messages = new List<Message> { message };
    messages.ShouldContainMessageWithContent("Test message");
}
```

## 4. TestConfigurationHelper.cs - 测试配置辅助类

### 主要功能

- **统一配置管理**：`GetConfiguration()`
- **临时配置文件**：`CreateTempConfigFile()`
- **标准配置对象**：`GetTestBotConfig()`, `GetTestLLMChannels()`
- **环境变量**：`GetTestEnvironmentVariables()`
- **配置验证**：`ValidateConfiguration()`

### 使用示例

```csharp
[Fact]
public void Example_TestConfigurationHelper()
{
    // 获取测试配置
    var botConfig = TestConfigurationHelper.GetTestBotConfig();
    Assert.Equal("test_bot_token_123456789", botConfig.BotToken);
    
    // 获取LLM通道配置
    var llmChannels = TestConfigurationHelper.GetTestLLMChannels();
    Assert.Equal(3, llmChannels.Count);
    
    // 创建临时配置文件
    var configPath = TestConfigurationHelper.CreateTempConfigFile();
    Assert.True(File.Exists(configPath));
    
    // 清理
    TestConfigurationHelper.CleanupTempConfigFile();
}
```

## 5. IntegrationTestBase.cs - 集成测试基类

### 主要功能

- **完整的服务容器**：自动配置所有依赖服务
- **标准测试数据**：自动创建测试数据集
- **Mock服务**：预配置的Mock对象
- **模拟操作**：`SimulateBotMessageReceivedAsync()`, `SimulateSearchRequestAsync()`
- **数据库管理**：快照、恢复、验证功能
- **资源清理**：自动释放资源

### 使用示例

```csharp
public class MyIntegrationTest : IntegrationTestBase
{
    [Fact]
    public async Task Example_IntegrationTest()
    {
        // 使用基类提供的测试数据
        Assert.NotNull(_testData);
        Assert.Equal(3, _testData.Messages.Count);
        
        // 模拟Bot消息接收
        var message = MessageTestDataFactory.CreateValidMessageOption();
        await SimulateBotMessageReceivedAsync(message);
        
        // 模拟搜索请求
        var results = await SimulateSearchRequestAsync("test", 100);
        Assert.NotNull(results);
        
        // 验证数据库状态
        await ValidateDatabaseStateAsync(3, 3, 2);
    }
}
```

## 完整测试示例

参考 `TestToolsExample.cs` 文件，它包含了所有测试工具的综合使用示例。

## 最佳实践

### 1. 测试组织

```csharp
// 使用集成测试基类
public class MessageServiceTests : IntegrationTestBase
{
    [Fact]
    public async Task ProcessMessage_ShouldHandleSpecialCharacters()
    {
        // Arrange
        var message = new MessageOptionBuilder()
            .WithUserId(1)
            .WithChatId(100)
            .WithContent("Message with 中文 and emoji 😊")
            .Build();
        
        // Act
        await SimulateBotMessageReceivedAsync(message);
        
        // Assert
        var processed = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.MessageId == message.MessageId);
        
        processed.ShouldNotBeNull();
        processed.Content.ShouldContainChinese();
        processed.Content.ShouldContainEmoji();
    }
}
```

### 2. 性能测试

```csharp
[Fact]
public async Task BatchProcessing_ShouldBeFast()
{
    var messages = Enumerable.Range(1, 100)
        .Select(i => MessageTestDataFactory.CreateValidMessageOption(
            messageId: 1000 + i))
        .ToList();
    
    var startTime = DateTime.UtcNow;
    
    foreach (var message in messages)
    {
        await SimulateBotMessageReceivedAsync(message);
    }
    
    var duration = DateTime.UtcNow - startTime;
    Assert.True(duration.TotalSeconds < 5, 
        $"Batch processing took {duration.TotalSeconds}s, expected < 5s");
}
```

### 3. 错误处理测试

```csharp
[Fact]
public async Task LLMServiceError_ShouldBeHandledGracefully()
{
    // 配置LLM服务抛出异常
    _llmServiceMock.Setup(x => x.ChatCompletionAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("Service unavailable"));
    
    // 验证异常处理
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        SimulateLLMRequestAsync("test", "response"));
}
```

## 依赖关系

```
IntegrationTestBase
├── TestDatabaseHelper
├── MockServiceFactory
├── TestConfigurationHelper
└── TestAssertionExtensions
```

## 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定测试类
dotnet test --filter "TestToolsExample"

# 运行性能测试
dotnet test --filter "Performance"
```

## 注意事项

1. **简化实现**：测试工具中的某些功能使用简化实现，以便于测试
2. **内存数据库**：所有测试使用InMemory数据库，不会影响实际数据
3. **自动清理**：集成测试基类会自动清理资源
4. **线程安全**：测试工具支持并行测试执行

## 扩展指南

### 添加新的Mock服务

```csharp
// 在MockServiceFactory中添加
public static Mock<IService> CreateServiceMock(Action<Mock<IService>>? configure = null)
{
    var mock = new Mock<IService>();
    // 默认配置
    configure?.Invoke(mock);
    return mock;
}
```

### 添加新的断言扩展

```csharp
// 在TestAssertionExtensions中添加
public static void ShouldBeValid(this MyObject obj, string expectedProperty)
{
    Assert.NotNull(obj);
    Assert.Equal(expectedProperty, obj.Property);
}
```

### 添加新的配置类型

```csharp
// 在TestConfigurationHelper中添加
public static MyConfig GetTestMyConfig()
{
    return new MyConfig
    {
        Property1 = "test_value",
        Property2 = 123
    };
}
```

通过使用这些测试工具，你可以显著提高测试开发效率，减少重复代码，并确保测试的一致性和可维护性。