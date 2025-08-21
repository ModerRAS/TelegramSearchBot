using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Tests;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Test.Helpers;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Xunit;
using Xunit.Abstractions;

namespace TelegramSearchBot.Test.Integration
{
    /// <summary>
    /// Message领域数据库集成测试
    /// 测试TelegramSearchBot.Domain.Message.MessageRepository与真实数据库的交互
    /// 
    /// 原本实现：使用Mock数据库进行单元测试
    /// 简化实现：使用SQLite内存数据库进行集成测试
    /// 限制：没有测试并发访问和事务隔离级别
    /// </summary>
    public class MessageDatabaseIntegrationTests : IDisposable
    {
        private readonly DataDbContext _context;
        private readonly TelegramSearchBot.Domain.Message.MessageRepository _repository;
        private readonly ITestOutputHelper _output;

        public MessageDatabaseIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            
            // 使用SQLite内存数据库进行真实数据库操作测试
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"MessageIntegrationTest_{Guid.NewGuid()}")
                .EnableSensitiveDataLogging()
                .Options;
            
            _context = new DataDbContext(options);
            // 简化实现：使用null logger，在测试环境中避免复杂的日志配置
            _repository = new TelegramSearchBot.Domain.Message.MessageRepository(_context, null);
            
            _output.WriteLine($"[Setup] Created test database: {_context.Database.GetDbConnection().Database}");
        }

        [Fact]
        public async Task AddMessageAsync_WithValidMessage_ShouldPersistToDatabase()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(
                groupId: 100L,
                messageId: 1000L,
                content: "Integration test message");
            
            // Act
            var result = await _repository.AddMessageAsync(message);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(message.GroupId, result.GroupId);
            Assert.Equal(message.MessageId, result.MessageId);
            
            // 验证数据库中确实存在
            var dbMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1000L);
            
            Assert.NotNull(dbMessage);
            Assert.Equal("Integration test message", dbMessage.Content);
            
            _output.WriteLine($"[Test] Successfully added message to database: {dbMessage.MessageId}");
        }

        [Fact]
        public async Task GetMessageByIdAsync_WithExistingId_ShouldReturnMessage()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(
                groupId: 100L,
                messageId: 1001L,
                content: "Test message for retrieval");
            
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            
            // Act
            // 简化实现：使用新的DDD架构的MessageId参数
            var messageId = new MessageId(100L, 1001L);
            var result = await _repository.GetByIdAsync(messageId);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(100L, result.Id.ChatId);
            Assert.Equal(1001L, result.Id.TelegramMessageId);
            Assert.Equal("Test message for retrieval", result.Content.Value);
            
            _output.WriteLine($"[Test] Retrieved message: {result.Content.Value}");
        }

        [Fact]
        public async Task UpdateMessageContentAsync_WithValidData_ShouldUpdateDatabase()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(
                groupId: 100L,
                messageId: 1002L,
                content: "Original content");
            
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            
            // Act
            // 简化实现：使用DDD架构的UpdateAsync方法
            var messageId = new MessageId(100L, 1002L);
            var aggregate = MessageAggregate.Create(100L, 1002L, "Updated content", message.FromUserId, DateTime.UtcNow);
            await _repository.UpdateAsync(aggregate);
            
            // Assert
            // 验证数据库中的内容已更新
            var dbMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1002L);
            
            Assert.NotNull(dbMessage);
            Assert.Equal("Updated content", dbMessage.Content);
            
            _output.WriteLine($"[Test] Updated message content: {dbMessage.Content}");
        }

        [Fact]
        public async Task DeleteMessageAsync_WithExistingMessage_ShouldRemoveFromDatabase()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(
                groupId: 100L,
                messageId: 1003L,
                content: "Message to delete");
            
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            
            // 确保消息存在
            var existsBefore = await _context.Messages
                .AnyAsync(m => m.GroupId == 100L && m.MessageId == 1003L);
            Assert.True(existsBefore);
            
            // Act
            // 简化实现：使用DDD架构的DeleteAsync方法
            var messageId = new MessageId(100L, 1003L);
            await _repository.DeleteAsync(messageId);
            
            // Assert
            // 简化实现：DeleteAsync是void方法，不返回结果
            
            // 验证消息已从数据库删除
            var existsAfter = await _context.Messages
                .AnyAsync(m => m.GroupId == 100L && m.MessageId == 1003L);
            Assert.False(existsAfter);
            
            _output.WriteLine("[Test] Successfully deleted message from database");
        }

        [Fact]
        public async Task GetMessagesByGroupIdAsync_WithMultipleMessages_ShouldReturnCorrectCount()
        {
            // Arrange
            var messages = new List<TelegramSearchBot.Model.Data.Message>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1010L + i,
                    content: $"Message {i} in group 100"));
            }
            
            // 添加其他群组的消息
            messages.Add(MessageTestDataFactory.CreateValidMessage(
                groupId: 200L,
                messageId: 2000L,
                content: "Message in different group"));
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            // Act
            var results = await _repository.GetMessagesByGroupIdAsync(100L);
            
            // Assert
            Assert.NotNull(results);
            Assert.Equal(10, results.Count());
            // 简化实现：使用DDD架构的Id.ChatId属性
            Assert.All(results, m => Assert.Equal(100L, m.Id.ChatId));
            
            _output.WriteLine($"[Test] Retrieved {results.Count()} messages for group 100");
        }

        [Fact]
        public async Task GetMessagesByUserIdAsync_WithUserMessages_ShouldReturnUserMessages()
        {
            // Arrange
            var userMessages = new List<TelegramSearchBot.Model.Data.Message>();
            for (int i = 0; i < 5; i++)
            {
                userMessages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1020L + i,
                    userId: 12345L,
                    content: $"User message {i}"));
            }
            
            // 添加其他用户的消息
            userMessages.Add(MessageTestDataFactory.CreateValidMessage(
                groupId: 100L,
                messageId: 1030L,
                userId: 67890L,
                content: "Other user message"));
            
            await _context.Messages.AddRangeAsync(userMessages);
            await _context.SaveChangesAsync();
            
            // Act
            var results = await _repository.GetMessagesByUserIdAsync(12345L);
            
            // Assert
            Assert.NotNull(results);
            Assert.Equal(5, results.Count());
            Assert.All(results, m => Assert.Equal(12345L, m.FromUserId));
            
            _output.WriteLine($"[Test] Retrieved {results.Count()} messages for user 12345");
        }

        [Fact]
        public async Task GetMessagesByDateRangeAsync_WithDateRange_ShouldReturnMessagesInRange()
        {
            // Arrange
            var baseDate = DateTime.UtcNow;
            var messages = new List<TelegramSearchBot.Model.Data.Message>
            {
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1040L,
                    content: "Message before range",
                    dateTime: baseDate.AddDays(-2)),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1041L,
                    content: "Message in range 1",
                    dateTime: baseDate.AddDays(-1)),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1042L,
                    content: "Message in range 2",
                    dateTime: baseDate),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1043L,
                    content: "Message after range",
                    dateTime: baseDate.AddDays(1))
            };
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            var startDate = baseDate.AddDays(-1);
            var endDate = baseDate;
            
            // Act
            // 简化实现：添加必需的groupId参数
            var results = await _repository.GetMessagesByDateRangeAsync(100L, startDate, endDate);
            
            // Assert
            Assert.NotNull(results);
            Assert.Equal(2, results.Count());
            Assert.Contains(results, m => m.Content.Contains("in range 1"));
            Assert.Contains(results, m => m.Content.Contains("in range 2"));
            
            _output.WriteLine($"[Test] Retrieved {results.Count()} messages in date range");
        }

        [Fact]
        public async Task SearchMessagesAsync_WithKeyword_ShouldReturnMatchingMessages()
        {
            // Arrange
            var messages = new List<TelegramSearchBot.Model.Data.Message>
            {
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1050L,
                    content: "This is a test message about integration"),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1051L,
                    content: "Another test message for search"),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1052L,
                    content: "Different content without keyword")
            };
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            // Act
            var results = await _repository.SearchMessagesAsync(100L, "test", 10);
            
            // Assert
            Assert.NotNull(results);
            Assert.Equal(2, results.Count());
            // 简化实现：使用DDD架构的Content.Value属性
            Assert.All(results, m => Assert.Contains("test", m.Content.Value.ToLower()));
            
            _output.WriteLine($"[Test] Found {results.Count()} messages containing 'test'");
        }

        [Fact]
        public async Task GetLatestMessageByGroupIdAsync_WithMultipleMessages_ShouldReturnLatest()
        {
            // Arrange
            var baseDate = DateTime.UtcNow;
            var messages = new List<TelegramSearchBot.Model.Data.Message>
            {
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1060L,
                    content: "First message",
                    dateTime: baseDate.AddMinutes(-10)),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1061L,
                    content: "Latest message",
                    dateTime: baseDate.AddMinutes(-5)),
                
                MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1062L,
                    content: "Middle message",
                    dateTime: baseDate.AddMinutes(-7))
            };
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            // Act
            var result = await _repository.GetLatestMessageByGroupIdAsync(100L);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(1061L, result.MessageId);
            Assert.Equal("Latest message", result.Content);
            
            _output.WriteLine($"[Test] Latest message: {result.Content} at {result.DateTime}");
        }

        [Fact]
        public async Task GetMessageCountByGroupIdAsync_WithMessages_ShouldReturnCorrectCount()
        {
            // Arrange
            var messages = new List<TelegramSearchBot.Model.Data.Message>();
            for (int i = 0; i < 15; i++)
            {
                messages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 1070L + i,
                    content: $"Count test message {i}"));
            }
            
            // 添加其他群组的消息
            messages.Add(MessageTestDataFactory.CreateValidMessage(
                groupId: 200L,
                messageId: 2000L,
                content: "Message in different group"));
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            // Act
            var count = await _repository.GetMessageCountByGroupIdAsync(100L);
            
            // Assert
            Assert.Equal(15, count);
            
            _output.WriteLine($"[Test] Group 100 has {count} messages");
        }

        [Fact]
        public async Task ConcurrentOperations_ShouldHandleConcurrencyCorrectly()
        {
            // Arrange
            var groupId = 100L;
            var baseMessageId = 1080L;
            var tasks = new List<Task<TelegramSearchBot.Model.Data.Message>>();
            
            // Act
            // 并行添加多个消息
            for (int i = 0; i < 5; i++)
            {
                var messageId = baseMessageId + i;
                var task = Task.Run(async () =>
                {
                    var message = MessageTestDataFactory.CreateValidMessage(
                        groupId: groupId,
                        messageId: messageId,
                        content: $"Concurrent message {i}");
                    return await _repository.AddMessageAsync(message);
                });
                tasks.Add(task);
            }
            
            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);
            
            // Assert
            Assert.Equal(5, results.Length);
            Assert.All(results, r => Assert.NotNull(r));
            
            // 验证数据库中的消息数量
            var dbCount = await _context.Messages
                .CountAsync(m => m.GroupId == groupId && m.MessageId >= baseMessageId);
            Assert.Equal(5, dbCount);
            
            _output.WriteLine($"[Test] Successfully handled {tasks.Count} concurrent operations");
        }

        [Fact]
        public async Task LargeDatasetOperations_ShouldPerformAcceptably()
        {
            // Arrange
            var messageCount = 1000;
            var messages = new List<TelegramSearchBot.Model.Data.Message>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // 生成测试数据
            for (int i = 0; i < messageCount; i++)
            {
                messages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100L,
                    messageId: 2000L + i,
                    content: $"Large dataset test message {i} with some additional content"));
            }
            
            // Act - 批量插入
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            var insertTime = stopwatch.ElapsedMilliseconds;
            
            // 测试查询性能
            stopwatch.Restart();
            var results = await _repository.GetMessagesByGroupIdAsync(100L);
            var queryTime = stopwatch.ElapsedMilliseconds;
            
            // 测试搜索性能
            stopwatch.Restart();
            var searchResults = await _repository.SearchMessagesAsync(100L, "test", 100);
            var searchTime = stopwatch.ElapsedMilliseconds;
            
            // Assert
            Assert.Equal(messageCount, results.Count());
            Assert.True(searchResults.Count() > 0);
            
            // 性能断言 - 这些阈值应根据实际硬件调整
            Assert.True(insertTime < 5000, $"Insert time {insertTime}ms should be less than 5000ms");
            Assert.True(queryTime < 1000, $"Query time {queryTime}ms should be less than 1000ms");
            Assert.True(searchTime < 500, $"Search time {searchTime}ms should be less than 500ms");
            
            _output.WriteLine($"[Performance] Inserted {messageCount} messages in {insertTime}ms");
            _output.WriteLine($"[Performance] Queried {results.Count()} messages in {queryTime}ms");
            _output.WriteLine($"[Performance] Searched in {searchTime}ms, found {searchResults.Count()} results");
        }

        public void Dispose()
        {
            _context?.Dispose();
            _output.WriteLine("[Cleanup] Test database disposed");
        }
    }
}