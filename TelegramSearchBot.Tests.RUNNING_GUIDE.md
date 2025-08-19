# TelegramSearchBot DDD 测试套件运行指南

## 测试套件概述

本测试套件为TelegramSearchBot项目提供全面的DDD架构测试覆盖，包括：

### 1. DDD架构组件测试 (40%)
- **聚合根测试**：`MessageAggregateTests.cs` - 测试业务逻辑和领域事件
- **值对象测试**：`MessageIdTests.cs`, `MessageContentTests.cs`, `MessageMetadataTests.cs` - 测试不可变性和验证规则
- **领域事件测试**：`MessageEventsTests.cs` - 测试事件创建和属性验证
- **仓储模式测试**：`MessageRepositoryTests.cs` - 测试接口定义和行为

### 2. 业务逻辑测试 (30%)
- **MessageService测试**：`MessageServiceTests.cs` - 测试核心业务逻辑
- **消息处理流程测试**：集成测试中的完整流程测试
- **数据验证测试**：输入验证和业务规则测试
- **错误处理测试**：异常情况和边界条件测试

### 3. 集成测试 (20%)
- **消息处理集成测试**：`MessageProcessingIntegrationTests.cs` - 完整的端到端测试
- **依赖注入测试**：DI配置和服务注册测试
- **领域事件集成测试**：事件发布和处理的集成测试

### 4. 性能测试 (10%)
- **消息处理性能测试**：`MessageProcessingBenchmarks.cs` - 大量消息处理的性能基准测试
- **查询性能测试**：搜索和查询的性能测试

## 测试运行环境要求

### 必需的NuGet包
```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
<PackageReference Include="xunit" Version="2.6.6" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
```

### .NET版本要求
- .NET 9.0 或更高版本

## 测试运行方法

### 1. 运行所有单元测试
```bash
# 进入DDD领域测试目录
cd TelegramSearchBot.Domain.Tests

# 运行所有测试
dotnet test

# 运行特定测试类
dotnet test --filter "MessageAggregateTests"

# 运行特定测试方法
dotnet test --filter "MessageAggregateTests.Constructor_WithValidParameters_ShouldCreateMessageAggregate"
```

### 2. 运行集成测试
```bash
# 进入集成测试目录
cd TelegramSearchBot.Integration.Tests

# 运行所有集成测试
dotnet test

# 运行特定的集成测试
dotnet test --filter "MessageProcessingIntegrationTests"
```

### 3. 运行性能测试
```bash
# 进入性能测试目录
cd TelegramSearchBot.Performance.Tests

# 运行性能基准测试
dotnet run --configuration Release

# 运行特定的性能测试
dotnet run --configuration Release --filter "MessageProcessingBenchmarks"
```

### 4. 运行所有测试并生成覆盖率报告
```bash
# 在解决方案根目录运行
dotnet test --collect:"XPlat Code Coverage"

# 生成HTML覆盖率报告
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
reportgenerator -reports:TestResults/coverage.xml -targetdir:TestResults/Report
```

## 测试数据工厂

### MessageAggregateTestDataFactory
提供标准的测试数据生成方法：

```csharp
// 创建标准消息
var message = MessageAggregateTestDataFactory.CreateStandardMessage();

// 创建带回复的消息
var replyMessage = MessageAggregateTestDataFactory.CreateMessageWithReply();

// 创建长文本消息
var longMessage = MessageAggregateTestDataFactory.CreateLongMessage();

// 创建多个消息
var messages = MessageAggregateTestDataFactory.CreateMultipleMessages(100);
```

### 测试基类
- `DomainTestBase`：提供通用的测试设置和验证方法
- `IntegrationTestBase`：提供集成测试的共享设置

## 测试覆盖率目标

### 覆盖率要求
- **单元测试**：90%+ 行覆盖率
- **集成测试**：80%+ 功能覆盖率
- **整体覆盖率**：85%+

### 覆盖率检查命令
```bash
# 检查DDD领域测试覆盖率
dotnet test TelegramSearchBot.Domain.Tests --collect:"XPlat Code Coverage"

# 检查集成测试覆盖率
dotnet test TelegramSearchBot.Integration.Tests --collect:"XPlat Code Coverage"
```

## 性能基准测试

### 运行性能测试
```bash
# 运行所有性能测试
dotnet run --project TelegramSearchBot.Performance.Tests --configuration Release

# 生成性能报告
dotnet run --project TelegramSearchBot.Performance.Tests --configuration Release --artifacts ./BenchmarkResults
```

### 性能测试指标
- **消息聚合创建**：< 1ms 平均时间
- **值对象操作**：< 0.1ms 平均时间
- **搜索操作**：< 10ms (1000条记录)
- **批量处理**：< 100ms (10000条记录)

## 持续集成配置

### GitHub Actions配置示例
```yaml
name: Test Suite

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x
      - name: Run unit tests
        run: dotnet test TelegramSearchBot.Domain.Tests --collect:"XPlat Code Coverage"
      - name: Upload coverage to Codecov
        uses: codecov/codecov-action@v3

  integration-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x
      - name: Run integration tests
        run: dotnet test TelegramSearchBot.Integration.Tests

  performance-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x
      - name: Run performance tests
        run: dotnet run --project TelegramSearchBot.Performance.Tests --configuration Release
```

## 故障排除

### 常见问题

1. **测试依赖问题**
   ```bash
   # 清理并重建
   dotnet clean
   dotnet restore
   dotnet build
   ```

2. **测试运行失败**
   ```bash
   # 详细输出
   dotnet test --verbosity normal
   
   # 调试模式
   dotnet test --logger "console;verbosity=detailed"
   ```

3. **性能测试问题**
   ```bash
   # 确保在Release模式下运行
   dotnet run --configuration Release
   
   # 检查.NET版本
   dotnet --version
   ```

### 测试调试技巧

1. **在Visual Studio中调试**
   - 右键点击测试方法，选择"调试测试"
   - 使用断点和调试窗口

2. **使用命令行调试**
   ```bash
   # 运行特定测试并输出详细信息
   dotnet test --filter "TestMethodName" --logger "console;verbosity=detailed"
   ```

3. **内存泄漏检测**
   ```bash
   # 使用诊断工具
   dotnet test --collect:"XPlat Code Coverage" --diag:diag.log
   ```

## 测试报告生成

### 生成HTML报告
```bash
# 安装报告生成器
dotnet tool install -g dotnet-reportgenerator-globaltool

# 生成报告
reportgenerator -reports:coverage.xml -targetdir:Report -reporttypes:Html
```

### 生成性能报告
性能测试会自动生成以下报告：
- `BenchmarkDotNet.Artifacts/results/` - 包含详细的性能数据
- HTML格式的性能报告
- CSV格式的数据导出

## 最佳实践

### 1. 编写测试的最佳实践
- 使用描述性的测试名称
- 遵循AAA模式（Arrange-Act-Assert）
- 使用测试数据工厂
- 避免测试之间的依赖

### 2. 性能测试的最佳实践
- 在Release模式下运行
- 预热JIT编译器
- 运行多次以获得稳定结果
- 监控内存使用

### 3. 集成测试的最佳实践
- 使用真实的依赖关系
- 测试完整的业务流程
- 包含错误场景测试
- 清理测试数据

## 测试套件维护

### 添加新测试
1. 在相应的测试类中添加测试方法
2. 遵循现有的命名约定
3. 使用现有的测试工具和基类
4. 更新测试数据工厂（如果需要）

### 更新测试数据
1. 修改`MessageAggregateTestDataFactory`
2. 确保测试数据的多样性
3. 包含边界条件和异常情况

### 性能基准更新
1. 定期运行性能测试
2. 监控性能回归
3. 更新性能目标（如果需要）
4. 记录性能改进