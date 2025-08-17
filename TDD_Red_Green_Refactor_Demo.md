# TDD Red-Green-Refactor 实战演示

## 概述

本文档演示了为TelegramSearchBot项目的Message领域实施TDD（测试驱动开发）的完整过程，包括Red（写失败的测试）、Green（实现功能）、Refactor（重构）三个阶段的详细步骤。

## 场景：Message搜索功能开发

### 需求分析
我们需要为Message领域添加一个新的搜索功能，支持：
1. 按关键词搜索消息内容
2. 支持大小写不敏感搜索
3. 支持结果数量限制
4. 支持特定群组内的搜索

## Red阶段：编写失败的测试

### Step 1: 创建MessageSearchServiceTests.cs

```csharp
// 文件：TelegramSearchBot.Test/Domain/Message/MessageSearchServiceTests.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageSearchServiceTests : TestBase
    {
        private readonly Mock<DataDbContext> _mockDbContext;
        private readonly Mock<DbSet<Message>> _mockMessagesDbSet;

        public MessageSearchServiceTests()
        {
            _mockDbContext = CreateMockDbContext();
            _mockMessagesDbSet = new Mock<DbSet<Message>>();
        }

        [Fact]
        public async Task SearchMessagesAsync_WithKeyword_ShouldReturnMatchingMessages()
        {
            // Arrange
            var groupId = 100L;
            var keyword = "search";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, "This is a search test"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, "Another message"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, "Search functionality")
            };

            SetupMockMessagesDbSet(messages);

            var searchService = CreateSearchService();

            // Act
            var result = await searchService.SearchMessagesAsync(groupId, keyword);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.Contains(keyword, m.Content, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SearchMessagesAsync_WithEmptyKeyword_ShouldReturnAllMessages()
        {
            // Arrange
            var groupId = 100L;
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001)
            };

            SetupMockMessagesDbSet(messages);

            var searchService = CreateSearchService();

            // Act
            var result = await searchService.SearchMessagesAsync(groupId, "");

            // Assert
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task SearchMessagesAsync_WithLimit_ShouldReturnLimitedResults()
        {
            // Arrange
            var groupId = 100L;
            var keyword = "test";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, "test 1"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, "test 2"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, "test 3"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1003, "test 4")
            };

            SetupMockMessagesDbSet(messages);

            var searchService = CreateSearchService();

            // Act
            var result = await searchService.SearchMessagesAsync(groupId, keyword, limit: 2);

            // Assert
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task SearchMessagesAsync_CaseInsensitive_ShouldIgnoreCase()
        {
            // Arrange
            var groupId = 100L;
            var keyword = "SEARCH";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(groupId, 1000, "this is a search test"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1001, "another message"),
                MessageTestDataFactory.CreateValidMessage(groupId, 1002, "Search functionality")
            };

            SetupMockMessagesDbSet(messages);

            var searchService = CreateSearchService();

            // Act
            var result = await searchService.SearchMessagesAsync(groupId, keyword);

            // Assert
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task SearchMessagesAsync_NonExistingGroup_ShouldReturnEmptyList()
        {
            // Arrange
            var groupId = 999L;
            var keyword = "test";
            
            var messages = new List<Message>
            {
                MessageTestDataFactory.CreateValidMessage(100, 1000, "test message")
            };

            SetupMockMessagesDbSet(messages);

            var searchService = CreateSearchService();

            // Act
            var result = await searchService.SearchMessagesAsync(groupId, keyword);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchMessagesAsync_NullKeyword_ShouldThrowArgumentNullException()
        {
            // Arrange
            var groupId = 100L;
            var searchService = CreateSearchService();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                searchService.SearchMessagesAsync(groupId, null));
        }

        private MessageSearchService CreateSearchService()
        {
            return new MessageSearchService(_mockDbContext.Object);
        }

        private void SetupMockMessagesDbSet(List<Message> messages)
        {
            var queryable = messages.AsQueryable();
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.Provider).Returns(queryable.Provider);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.Expression).Returns(queryable.Expression);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            _mockMessagesDbSet.As<IQueryable<Message>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            _mockDbContext.Setup(ctx => ctx.Messages).Returns(_mockMessagesDbSet.Object);
        }
    }
}
```

### Step 2: 创建MessageSearchService接口（简化实现）

```csharp
// 文件：TelegramSearchBot/Domain/Message/IMessageSearchService.cs

using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelegramSearchBot.Domain.Message
{
    public interface IMessageSearchService
    {
        Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50);
    }
}
```

### Step 3: 创建MessageSearchService最小实现（确保编译通过）

```csharp
// 文件：TelegramSearchBot/Domain/Message/MessageSearchService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Message
{
    public class MessageSearchService : IMessageSearchService
    {
        private readonly DataDbContext _context;

        public MessageSearchService(DataDbContext context)
        {
            _context = context;
        }

        public Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
        {
            // 简化实现：返回空列表，确保测试失败
            return Task.FromResult<IEnumerable<Message>>(new List<Message>());
        }
    }
}
```

### Step 4: 运行测试（预期失败）

```bash
# 运行测试，预期所有测试都失败
dotnet test TelegramSearchBot.Test.csproj --filter "MessageSearchServiceTests"

# 预期结果：
# - SearchMessagesAsync_WithKeyword_ShouldReturnMatchingMessages 失败
# - SearchMessagesAsync_WithEmptyKeyword_ShouldReturnAllMessages 失败
# - SearchMessagesAsync_WithLimit_ShouldReturnLimitedResults 失败
# - SearchMessagesAsync_CaseInsensitive_ShouldIgnoreCase 失败
# - SearchMessagesAsync_NonExistingGroup_ShouldReturnEmptyList 失败
# - SearchMessagesAsync_NullKeyword_ShouldThrowArgumentNullException 失败
```

## Green阶段：实现功能使测试通过

### Step 1: 实现基本搜索功能

```csharp
// 更新 MessageSearchService.cs

public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
{
    if (keyword == null)
        throw new ArgumentNullException(nameof(keyword));

    var query = _context.Messages.Where(m => m.GroupId == groupId);
    
    if (!string.IsNullOrEmpty(keyword))
    {
        query = query.Where(m => m.Content.Contains(keyword));
    }
    
    return await query.Take(limit).ToListAsync();
}
```

### Step 2: 运行测试（部分通过）

```bash
# 运行测试，部分测试通过
dotnet test TelegramSearchBot.Test.csproj --filter "MessageSearchServiceTests"

# 预期结果：
# - SearchMessagesAsync_WithKeyword_ShouldReturnMatchingMessages 通过
# - SearchMessagesAsync_WithEmptyKeyword_ShouldReturnAllMessages 通过
# - SearchMessagesAsync_WithLimit_ShouldReturnLimitedResults 通过
# - SearchMessagesAsync_NonExistingGroup_ShouldReturnEmptyList 通过
# - SearchMessagesAsync_NullKeyword_ShouldThrowArgumentNullException 通过
# - SearchMessagesAsync_CaseInsensitive_ShouldIgnoreCase 失败（大小写敏感问题）
```

### Step 3: 修复大小写敏感问题

```csharp
// 更新 MessageSearchService.cs

public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
{
    if (keyword == null)
        throw new ArgumentNullException(nameof(keyword));

    var query = _context.Messages.Where(m => m.GroupId == groupId);
    
    if (!string.IsNullOrEmpty(keyword))
    {
        query = query.Where(m => m.Content.ToUpper().Contains(keyword.ToUpper()));
    }
    
    return await query.Take(limit).ToListAsync();
}
```

### Step 4: 运行测试（全部通过）

```bash
# 运行测试，所有测试通过
dotnet test TelegramSearchBot.Test.csproj --filter "MessageSearchServiceTests"

# 预期结果：所有测试都通过！
```

## Refactor阶段：重构优化代码

### Step 1: 识别重构机会

当前实现的问题：
1. 字符串转换效率问题（ToUpper()可能影响性能）
2. 代码重复（Contains逻辑可以提取）
3. 缺少参数验证
4. 查询逻辑可以优化

### Step 2: 重构代码

```csharp
// 重构后的 MessageSearchService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Message
{
    public class MessageSearchService : IMessageSearchService
    {
        private readonly DataDbContext _context;
        private readonly ILogger<MessageSearchService> _logger;

        public MessageSearchService(DataDbContext context, ILogger<MessageSearchService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50)
        {
            // 参数验证
            if (groupId <= 0)
                throw new ArgumentException("Group ID must be greater than 0", nameof(groupId));
            
            if (string.IsNullOrEmpty(keyword))
                return await GetAllMessagesInGroupAsync(groupId, limit);

            if (limit <= 0 || limit > 1000)
                throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));

            try
            {
                return await SearchMessagesWithKeywordAsync(groupId, keyword, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId} with keyword '{Keyword}'", groupId, keyword);
                throw;
            }
        }

        private async Task<IEnumerable<Message>> GetAllMessagesInGroupAsync(long groupId, int limit)
        {
            return await _context.Messages
                .AsNoTracking()
                .Where(m => m.GroupId == groupId)
                .OrderByDescending(m => m.DateTime)
                .Take(limit)
                .ToListAsync();
        }

        private async Task<IEnumerable<Message>> SearchMessagesWithKeywordAsync(long groupId, string keyword, int limit)
        {
            // 使用 EF.Functions.Like 进行更高效的搜索
            return await _context.Messages
                .AsNoTracking()
                .Where(m => m.GroupId == groupId && 
                           EF.Functions.Like(m.Content, $"%{keyword}%"))
                .OrderByDescending(m => m.DateTime)
                .Take(limit)
                .ToListAsync();
        }
    }
}
```

### Step 3: 添加新的测试用例

```csharp
// 添加到 MessageSearchServiceTests.cs

[Fact]
public async Task SearchMessagesAsync_WithInvalidGroupId_ShouldThrowArgumentException()
{
    // Arrange
    var invalidGroupId = -1L;
    var searchService = CreateSearchService();

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => 
        searchService.SearchMessagesAsync(invalidGroupId, "test"));
}

[Fact]
public async Task SearchMessagesAsync_WithInvalidLimit_ShouldThrowArgumentException()
{
    // Arrange
    var groupId = 100L;
    var invalidLimit = 0;
    var searchService = CreateSearchService();

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => 
        searchService.SearchMessagesAsync(groupId, "test", limit: invalidLimit));
}

[Fact]
public async Task SearchMessagesAsync_WithLargeLimit_ShouldReturnLimitedResults()
{
    // Arrange
    var groupId = 100L;
    var keyword = "test";
    var largeLimit = 1000;
    
    var messages = Enumerable.Range(1, 1500)
        .Select(i => MessageTestDataFactory.CreateValidMessage(groupId, i, $"test {i}"))
        .ToList();

    SetupMockMessagesDbSet(messages);

    var searchService = CreateSearchService();

    // Act
    var result = await searchService.SearchMessagesAsync(groupId, keyword, limit: largeLimit);

    // Assert
    Assert.Equal(1000, result.Count());
}
```

### Step 4: 更新接口（添加依赖注入）

```csharp
// 更新 IMessageSearchService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Domain.Message
{
    public interface IMessageSearchService
    {
        Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50);
    }
}
```

### Step 5: 运行所有测试（确保重构后测试仍然通过）

```bash
# 运行所有测试
dotnet test TelegramSearchBot.Test.csproj --filter "MessageSearchServiceTests"

# 预期结果：所有测试都通过，包括新增的测试
```

## 总结

### TDD流程回顾

1. **Red阶段**：
   - 编写了6个失败的测试用例
   - 创建了最小化的接口和实现
   - 确认测试确实失败

2. **Green阶段**：
   - 实现了基本功能
   - 修复了大小写敏感问题
   - 确保所有测试通过

3. **Refactor阶段**：
   - 优化了代码结构
   - 添加了参数验证
   - 改进了错误处理
   - 添加了新的测试用例
   - 确保重构后测试仍然通过

### 关键收获

1. **测试先行的优势**：
   - 明确了功能需求
   - 确保代码质量
   - 提供了安全网，支持后续重构

2. **重构的重要性**：
   - 改善了代码结构
   - 提高了可维护性
   - 增强了错误处理

3. **持续改进**：
   - 根据测试用例不断完善功能
   - 通过测试驱动设计优化
   - 建立了可扩展的架构

### 最佳实践

1. **测试命名**：使用`UnitOfWork_StateUnderTest_ExpectedBehavior`模式
2. **AAA结构**：Arrange-Act-Assert清晰分离
3. **测试数据管理**：使用工厂模式和Builder模式
4. **Mock策略**：只Mock外部依赖，不Mock值对象
5. **重构时机**：在所有测试通过后进行重构

这个TDD实战演示展示了如何通过测试驱动开发构建高质量、可维护的代码，为TelegramSearchBot项目的Message领域提供了健壮的搜索功能。