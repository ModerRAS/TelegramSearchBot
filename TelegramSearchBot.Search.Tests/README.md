# TelegramSearchBot.Search.Tests

Search领域的全面测试套件，包含Lucene.NET全文搜索和FAISS向量搜索的单元测试、集成测试和性能测试。

## 🏗️ 项目结构

```
TelegramSearchBot.Search.Tests/
├── TelegramSearchBot.Search.Tests.csproj    # 测试项目配置
├── Base/
│   └── SearchTestBase.cs                     # 搜索测试基类
├── Lucene/
│   └── LuceneManagerTests.cs                 # Lucene搜索测试
├── Vector/
│   └── FaissVectorServiceTests.cs            # FAISS向量搜索测试
├── Integration/
│   └── SearchServiceIntegrationTests.cs      # 搜索服务集成测试
├── Performance/
│   └── SearchPerformanceTests.cs             # 搜索性能测试
├── Helpers/
│   └── SearchTestHelpers.cs                  # 测试辅助类和扩展方法
└── README.md                                 # 本文档
```

## 🎯 测试覆盖范围

### 1. Lucene搜索测试 (LuceneManagerTests.cs)
- **索引管理**: 创建、检查、删除索引
- **文档操作**: 添加、更新、删除文档
- **基本搜索**: 关键词搜索、大小写敏感、多关键词
- **分页功能**: Skip/Take参数验证
- **跨群组搜索**: SearchAll方法测试
- **语法搜索**: AND、OR、NOT等复杂查询
- **边界情况**: 空关键词、特殊字符、Unicode字符
- **性能测试**: 大数据量搜索、并发搜索

### 2. FAISS向量搜索测试 (FaissVectorServiceTests.cs)
- **向量索引管理**: 创建、检查、删除索引
- **向量操作**: 添加、更新、删除向量
- **批量操作**: 批量添加向量
- **相似度搜索**: 基于余弦相似度的向量搜索
- **元数据管理**: 向量元数据的CRUD操作
- **参数验证**: 维度检查、参数错误处理
- **性能测试**: 高维向量搜索、批量操作性能

### 3. 搜索服务集成测试 (SearchServiceIntegrationTests.cs)
- **搜索类型切换**: InvertedIndex、SyntaxSearch、VectorSearch
- **跨群组搜索**: IsGroup参数控制
- **分页集成**: Skip/Take参数传递
- **错误处理**: 空值检查、异常处理
- **性能测试**: 大数据量搜索、并发搜索
- **向后兼容**: SimpleSearch方法测试

### 4. 搜索性能测试 (SearchPerformanceTests.cs)
- **索引性能**: 不同数据集大小的索引性能
- **搜索性能**: 简单搜索和复杂查询的性能
- **向量搜索性能**: 不同维度向量的搜索性能
- **并发性能**: 并发索引和搜索的性能
- **内存使用**: 大数据集的内存占用测试
- **性能基准**: 综合性能基准测试

## 🚀 运行测试

### 前置条件
- .NET 9.0 SDK
- 所有依赖项目已编译成功

### 运行所有测试
```bash
dotnet test TelegramSearchBot.Search.Tests.csproj
```

### 运行特定测试类别
```bash
# 只运行Lucene测试
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~Lucene"

# 只运行Vector测试
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~Vector"

# 只运行集成测试
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~Integration"

# 只运行性能测试
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~Performance"
```

### 运行特定测试方法
```bash
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~LuceneManagerTests.WriteDocumentAsync_ValidMessage_ShouldCreateIndex"
```

## 📊 测试数据管理

### 测试数据工厂
使用 `SearchTestDataFactory` 创建标准化的测试数据：

```csharp
// 创建单个测试消息
var message = SearchTestDataFactory.CreateLuceneTestMessage(100, 1000, 1, "Test content");

// 创建批量测试消息
var messages = SearchTestDataFactory.CreateBulkTestMessages(1000, 100);

// 创建多语言测试消息
var multiLangMessages = SearchTestDataFactory.CreateMultiLanguageMessages(100);

// 创建特殊字符测试消息
var specialMessages = SearchTestDataFactory.CreateSpecialCharacterMessages(100);
```

### 测试辅助方法
使用 `SearchTestHelper` 进行通用测试操作：

```csharp
// 批量索引消息
await SearchTestHelper.IndexMessagesAsync(luceneManager, messages);

// 验证搜索结果
SearchTestHelper.ValidateResultsContainKeyword(results, "search");

// 测量执行时间
var executionTime = await SearchTestHelper.MeasureExecutionTime(async () => 
{
    await luceneManager.Search("test", 100);
});
```

## 🔧 自定义测试配置

### 测试基类继承
所有测试类都继承自 `SearchTestBase`，提供以下功能：

```csharp
public class MyCustomSearchTests : SearchTestBase
{
    public MyCustomSearchTests(ITestOutputHelper output) : base(output)
    {
        // 自动配置的测试环境
        // - 测试数据库
        // - Lucene索引目录
        // - 向量索引目录
        // - 日志记录
        // - 依赖注入容器
    }
}
```

### 自定义测试数据
```csharp
// 使用测试基类的方法创建测试数据
var message = CreateTestMessage(100, 1000, 1, "Custom test message");
var messages = CreateBulkTestMessages(500, 100);
```

## 📈 性能测试

### 性能基准测试
运行综合性能基准测试：

```bash
dotnet test TelegramSearchBot.Search.Tests.csproj --filter "FullyQualifiedName~PerformanceBenchmark_Comprehensive"
```

### 性能指标
- **索引性能**: 消息/秒
- **搜索性能**: 毫秒/查询
- **内存使用**: MB/1000消息
- **并发性能**: 查询/秒

## 🛠️ 故障排除

### 常见问题

1. **编译错误**
   ```bash
   # 确保所有依赖项目已编译
   dotnet build TelegramSearchBot.sln
   ```

2. **测试失败**
   ```bash
   # 运行测试并查看详细输出
   dotnet test TelegramSearchBot.Search.Tests.csproj --verbosity normal
   ```

3. **权限问题**
   ```bash
   # 确保有临时目录写入权限
   chmod -R 755 /tmp
   ```

4. **内存不足**
   ```bash
   # 减少测试数据量
   export TEST_DATA_SIZE=100
   dotnet test TelegramSearchBot.Search.Tests.csproj
   ```

### 调试技巧

1. **启用详细日志**
   ```csharp
   // 在测试中使用ITestOutputHelper
   Output.WriteLine($"Debug information: {variable}");
   ```

2. **检查测试目录**
   ```csharp
   // 测试基类自动创建测试目录
   Output.WriteLine($"Test index root: {TestIndexRoot}");
   ```

3. **验证测试数据**
   ```csharp
   // 使用验证器检查结果
   results.ShouldNotBeEmpty();
   results.ShouldAllContain("expected_keyword");
   ```

## 🎯 扩展测试

### 添加新的测试用例
```csharp
[Fact]
public async Task MyCustomSearchTest_ShouldWorkCorrectly()
{
    // Arrange
    var message = SearchTestDataFactory.CreateLuceneTestMessage(100, 9999, 1, "Custom test");
    await _luceneManager.WriteDocumentAsync(message);

    // Act
    var results = await _luceneManager.Search("custom", 100);

    // Assert
    results.ShouldNotBeEmpty();
    results.ShouldAllContain("custom");
}
```

### 添加新的性能测试
```csharp
[Fact]
public async Task MyCustomPerformanceTest_ShouldMeetRequirements()
{
    // Arrange
    var messages = SearchTestDataFactory.CreateBulkTestMessages(1000, 100);
    await SearchTestHelper.IndexMessagesAsync(_luceneManager, messages);

    // Act
    var executionTimes = await SearchTestHelper.RepeatAndMeasureAsync(async () => 
    {
        await _luceneManager.Search("performance", 100);
    }, 100);

    // Assert
    var avgTime = executionTimes.Average();
    avgTime.Should().BeLessThan(10); // Should be less than 10ms
}
```

## 📝 测试最佳实践

1. **测试命名**: 使用 `UnitOfWork_StateUnderTest_ExpectedBehavior` 格式
2. **AAA模式**: 遵循 Arrange-Act-Assert 结构
3. **测试隔离**: 每个测试使用独立的测试目录
4. **资源清理**: 测试完成后自动清理资源
5. **性能基准**: 为关键操作设置性能预期
6. **错误处理**: 测试边界情况和异常处理
7. **文档记录**: 为复杂测试场景添加注释

## 🔗 相关项目

- `TelegramSearchBot.Search` - 搜索功能实现
- `TelegramSearchBot.Vector` - 向量搜索实现
- `TelegramSearchBot.Data` - 数据模型和DbContext
- `TelegramSearchBot.Test` - 通用测试基础设施

---

*此测试套件为TelegramSearchBot项目的搜索功能提供了完整的质量保证，确保Lucene搜索和FAISS向量搜索的稳定性和性能。*