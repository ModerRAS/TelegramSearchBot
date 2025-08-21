using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Xunit;
using Xunit.Abstractions;

namespace TelegramSearchBot.Test.Integration
{
    /// <summary>
    /// Message领域仓储集成测试
    /// 测试DDD仓储接口与真实数据库的交互
    /// 
    /// 原本实现：直接测试具体实现类的方法
    /// 简化实现：通过DDD仓储接口测试，更符合领域驱动设计
    /// 限制：没有测试事务回滚和复杂查询场景
    /// </summary>
    public class MessageRepositoryIntegrationTests : IDisposable
    {
        private readonly DataDbContext _context;
        private readonly IMessageRepository _repository;
        private readonly ITestOutputHelper _output;

        public MessageRepositoryIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            
            // 使用SQLite内存数据库进行真实数据库操作测试
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"MessageRepoIntegrationTest_{Guid.NewGuid()}")
                .EnableSensitiveDataLogging()
                .Options;
            
            _context = new DataDbContext(options);
            // 简化实现：使用完全限定名称避免歧义，并传入null logger
            _repository = new TelegramSearchBot.Domain.Message.MessageRepository(_context, null);
            
            _output.WriteLine($"[Setup] Created test database for repository integration test");
        }

        [Fact]
        public async Task AddAsync_WithValidAggregate_ShouldPersistToDatabase()
        {
            // Arrange
            var messageId = new MessageId(100L, 1000L);
            var content = new MessageContent("Repository integration test message");
            var metadata = new MessageMetadata(12345L, DateTime.UtcNow);
            var aggregate = new MessageAggregate(messageId, content, metadata);
            
            // Act
            var result = await _repository.AddAsync(aggregate);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(messageId.ChatId, result.Id.ChatId);
            Assert.Equal(messageId.TelegramMessageId, result.Id.TelegramMessageId);
            Assert.Equal("Repository integration test message", result.Content.Text);
            
            // 验证数据库中确实存在
            var dbMessage = await _context.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1000L);
            
            Assert.NotNull(dbMessage);
            Assert.Equal("Repository integration test message", dbMessage.Content);
            Assert.Equal(12345L, dbMessage.FromUserId);
            
            _output.WriteLine($"[Test] Successfully added aggregate to database: {dbMessage.MessageId}");
        }

        [Fact]
        public async Task GetByIdAsync_WithExistingId_ShouldReturnAggregate()
        {
            // Arrange
            var message = new TelegramSearchBot.Model.Data.Message
            {
                GroupId = 100L,
                MessageId = 1001L,
                Content = "Test message for repository",
                FromUserId = 12345L,
                DateTime = DateTime.UtcNow,
                Processed = false,
                Vectorized = false
            };
            
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            
            var messageId = new MessageId(100L, 1001L);
            
            // Act
            var result = await _repository.GetByIdAsync(messageId);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(100L, result.Id.ChatId);
            Assert.Equal(1001L, result.Id.TelegramMessageId);
            Assert.Equal("Test message for repository", result.Content.Text);
            Assert.Equal(12345L, result.Metadata.FromUserId);
            
            _output.WriteLine($"[Test] Retrieved aggregate: {result.Content.Text}");
        }

        [Fact]
        public async Task GetByGroupIdAsync_WithMultipleMessages_ShouldReturnAggregates()
        {
            // Arrange
            var messages = new List<TelegramSearchBot.Model.Data.Message>();
            for (int i = 0; i < 5; i++)
            {
                messages.Add(new TelegramSearchBot.Model.Data.Message
                {
                    GroupId = 100L,
                    MessageId = 1010L + i,
                    Content = $"Repository test message {i}",
                    FromUserId = 12345L + i,
                    DateTime = DateTime.UtcNow.AddMinutes(-i),
                    Processed = false,
                    Vectorized = false
                });
            }
            
            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            
            // Act
            var results = await _repository.GetByGroupIdAsync(100L);
            
            // Assert
            Assert.NotNull(results);
            var resultList = results.ToList();
            Assert.Equal(5, resultList.Count);
            Assert.All(resultList, a => Assert.Equal(100L, a.Id.ChatId));
            
            // 验证按时间降序排列
            for (int i = 0; i < resultList.Count - 1; i++)
            {
                Assert.True(resultList[i].Metadata.Timestamp >= resultList[i + 1].Metadata.Timestamp);
            }
            
            _output.WriteLine($"[Test] Retrieved {resultList.Count} aggregates for group 100");
        }

        [Fact]
        public async Task UpdateAsync_WithModifiedAggregate_ShouldUpdateDatabase()
        {
            // Arrange
            var originalMessage = new TelegramSearchBot.Model.Data.Message
            {
                GroupId = 100L,
                MessageId = 1020L,
                Content = "Original content",
                FromUserId = 12345L,
                DateTime = DateTime.UtcNow,
                Processed = false,
                Vectorized = false
            };
            
            await _context.Messages.AddAsync(originalMessage);
            await _context.SaveChangesAsync();
            
            var messageId = new MessageId(100L, 1020L);
            var aggregate = await _repository.GetByIdAsync(messageId);
            
            // 修改聚合内容
            aggregate.UpdateContent(new MessageContent("Updated content via repository"));
            
            // Act
            await _repository.UpdateAsync(aggregate);
            
            // Assert
            var dbMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1020L);
            
            Assert.NotNull(dbMessage);
            Assert.Equal("Updated content via repository", dbMessage.Content);
            
            _output.WriteLine($"[Test] Updated aggregate content: {dbMessage.Content}");
        }

        [Fact]
        public async Task DeleteAsync_WithExistingAggregate_ShouldRemoveFromDatabase()
        {
            // Arrange
            var message = new TelegramSearchBot.Model.Data.Message
            {
                GroupId = 100L,
                MessageId = 1030L,
                Content = "Message to delete via repository",
                FromUserId = 12345L,
                DateTime = DateTime.UtcNow,
                Processed = false,
                Vectorized = false
            };
            
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            
            var messageId = new MessageId(100L, 1030L);
            
            // 确保消息存在
            var existsBefore = await _context.Messages
                .AnyAsync(m => m.GroupId == 100L && m.MessageId == 1030L);
            Assert.True(existsBefore);
            
            // Act
            await _repository.DeleteAsync(messageId);
            
            // Assert
            var existsAfter = await _context.Messages
                .AnyAsync(m => m.GroupId == 100L && m.MessageId == 1030L);
            Assert.False(existsAfter);
            
            _output.WriteLine("[Test] Successfully deleted aggregate via repository");
        }

        [Fact]
        public async Task AddAsync_WithMessageExtension_ShouldPersistExtension()
        {
            // Arrange
            var messageId = new MessageId(100L, 1040L);
            var content = new MessageContent("Message with extension");
            var metadata = new MessageMetadata(12345L, DateTime.UtcNow);
            var aggregate = new MessageAggregate(messageId, content, metadata);
            
            // 添加扩展
            aggregate.AddExtension(new TelegramSearchBot.Model.Data.MessageExtension { ExtensionType = "OCR", ExtensionData = "This is OCR extracted text" });
            aggregate.AddExtension(new TelegramSearchBot.Model.Data.MessageExtension { ExtensionType = "Translation", ExtensionData = "Translated text content" });
            
            // Act
            var result = await _repository.AddAsync(aggregate);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Extensions.Count());
            
            // 验证数据库中的扩展
            var dbMessage = await _context.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1040L);
            
            Assert.NotNull(dbMessage);
            Assert.Equal(2, dbMessage.MessageExtensions.Count);
            Assert.Contains(dbMessage.MessageExtensions, e => e.ExtensionType == "OCR");
            Assert.Contains(dbMessage.MessageExtensions, e => e.ExtensionType == "Translation");
            
            _output.WriteLine($"[Test] Persisted message with {dbMessage.MessageExtensions.Count} extensions");
        }

        [Fact]
        public async Task GetByIdAsync_WithNonExistingId_ShouldReturnNull()
        {
            // Arrange
            var nonExistingId = new MessageId(999L, 99999L);
            
            // Act
            var result = await _repository.GetByIdAsync(nonExistingId);
            
            // Assert
            Assert.Null(result);
            
            _output.WriteLine("[Test] Correctly returned null for non-existing ID");
        }

        [Fact]
        public async Task GetByGroupIdAsync_WithNoMessages_ShouldReturnEmptyList()
        {
            // Arrange
            var emptyGroupId = 999L;
            
            // Act
            var results = await _repository.GetByGroupIdAsync(emptyGroupId);
            
            // Assert
            Assert.NotNull(results);
            Assert.Empty(results);
            
            _output.WriteLine("[Test] Correctly returned empty list for group with no messages");
        }

        [Fact]
        public async Task AddAsync_WithLongContent_ShouldTruncateAndSave()
        {
            // Arrange
            var longContent = new string('a', 5000); // 超过4000字符限制
            var messageId = new MessageId(100L, 1050L);
            var content = new MessageContent(longContent);
            var metadata = new MessageMetadata(12345L, DateTime.UtcNow);
            var aggregate = new MessageAggregate(messageId, content, metadata);
            
            // Act
            var result = await _repository.AddAsync(aggregate);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Content.Text.Length <= 4000);
            
            // 验证数据库中的内容被截断
            var dbMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.GroupId == 100L && m.MessageId == 1050L);
            
            Assert.NotNull(dbMessage);
            Assert.True(dbMessage.Content.Length <= 4000);
            
            _output.WriteLine($"[Test] Long content truncated from {longContent.Length} to {dbMessage.Content.Length} characters");
        }

        [Fact]
        public async Task ConcurrentOperations_OnSameGroup_ShouldHandleCorrectly()
        {
            // Arrange
            var groupId = 100L;
            var baseMessageId = 1060L;
            var tasks = new List<Task<MessageAggregate>>();
            
            // Act
            // 并行添加多个消息到同一群组
            for (int i = 0; i < 3; i++)
            {
                var messageId = new MessageId(groupId, baseMessageId + i);
                var content = new MessageContent($"Concurrent message {i}");
                var metadata = new MessageMetadata(12345L + i, DateTime.UtcNow);
                var aggregate = new MessageAggregate(messageId, content, metadata);
                
                var task = Task.Run(async () => await _repository.AddAsync(aggregate));
                tasks.Add(task);
            }
            
            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);
            
            // Assert
            Assert.Equal(3, results.Length);
            Assert.All(results, r => Assert.NotNull(r));
            
            // 验证所有消息都已保存
            var dbCount = await _context.Messages
                .CountAsync(m => m.GroupId == groupId && m.MessageId >= baseMessageId);
            Assert.Equal(3, dbCount);
            
            _output.WriteLine($"[Test] Successfully handled {tasks.Count} concurrent operations on same group");
        }

        public void Dispose()
        {
            _context?.Dispose();
            _output.WriteLine("[Cleanup] Repository integration test database disposed");
        }
    }
}