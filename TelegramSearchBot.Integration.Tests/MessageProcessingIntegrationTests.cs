using Xunit;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelegramSearchBot.Integration.Tests
{
    /// <summary>
    /// 消息处理流程的集成测试
    /// 测试DDD架构中完整的消息处理流程
    /// </summary>
    public class MessageProcessingIntegrationTests
    {
        private readonly IServiceProvider _serviceProvider;

        public MessageProcessingIntegrationTests()
        {
            // 创建服务集合
            var services = new ServiceCollection();
            
            // 注册领域服务
            services.AddScoped<IMessageRepository, InMemoryMessageRepository>();
            services.AddScoped<IMessageService, MessageService>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public async Task CompleteMessageProcessingFlow_ShouldWorkEndToEnd()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var messageRepository = _serviceProvider.GetRequiredService<IMessageRepository>();
            
            var messageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 1,
                Content = "这是一条测试消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            // Act - 处理消息
            var processedMessageId = await messageService.ProcessMessageAsync(messageOption);

            // Assert - 验证消息被正确处理
            Assert.Equal(messageOption.MessageId, processedMessageId);

            // 验证消息可以被检索到
            var retrievedMessage = await messageRepository.GetByIdAsync(
                new MessageId(messageOption.ChatId, messageOption.MessageId));
            
            Assert.NotNull(retrievedMessage);
            Assert.Equal(messageOption.Content, retrievedMessage.Content.Value);
            Assert.Equal(messageOption.UserId, retrievedMessage.Metadata.FromUserId);

            // 验证群组消息列表
            var groupMessages = await messageService.GetGroupMessagesAsync(messageOption.ChatId);
            Assert.Single(groupMessages);
            Assert.Equal(messageOption.Content, groupMessages.First().Content);

            // 验证搜索功能
            var searchResults = await messageService.SearchMessagesAsync(messageOption.ChatId, "测试");
            Assert.Single(searchResults);
            Assert.Equal(messageOption.Content, searchResults.First().Content);

            // 验证用户消息
            var userMessages = await messageService.GetUserMessagesAsync(messageOption.ChatId, messageOption.UserId);
            Assert.Single(userMessages);
            Assert.Equal(messageOption.Content, userMessages.First().Content);
        }

        [Fact]
        public async Task MessageUpdateFlow_ShouldWorkCorrectly()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var messageRepository = _serviceProvider.GetRequiredService<IMessageRepository>();
            
            var messageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 1,
                Content = "原始消息内容",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            // Act - 创建消息
            await messageService.ProcessMessageAsync(messageOption);

            // 更新消息内容
            string newContent = "更新后的消息内容";
            var updateResult = await messageService.UpdateMessageAsync(
                messageOption.ChatId, messageOption.MessageId, newContent);

            // Assert
            Assert.True(updateResult);

            // 验证消息被更新
            var updatedMessage = await messageRepository.GetByIdAsync(
                new MessageId(messageOption.ChatId, messageOption.MessageId));
            
            Assert.NotNull(updatedMessage);
            Assert.Equal(newContent, updatedMessage.Content.Value);

            // 验证搜索功能找到新内容
            var searchResults = await messageService.SearchMessagesAsync(messageOption.ChatId, "更新后");
            Assert.Single(searchResults);
            Assert.Equal(newContent, searchResults.First().Content);
        }

        [Fact]
        public async Task MessageDeleteFlow_ShouldWorkCorrectly()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var messageRepository = _serviceProvider.GetRequiredService<IMessageRepository>();
            
            var messageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 1,
                Content = "待删除的消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            // Act - 创建消息
            await messageService.ProcessMessageAsync(messageOption);

            // 删除消息
            var deleteResult = await messageService.DeleteMessageAsync(
                messageOption.ChatId, messageOption.MessageId);

            // Assert
            Assert.True(deleteResult);

            // 验证消息被删除
            var deletedMessage = await messageRepository.GetByIdAsync(
                new MessageId(messageOption.ChatId, messageOption.MessageId));
            
            Assert.Null(deletedMessage);

            // 验证群组消息列表为空
            var groupMessages = await messageService.GetGroupMessagesAsync(messageOption.ChatId);
            Assert.Empty(groupMessages);
        }

        [Fact]
        public async Task ReplyMessageProcessing_ShouldWorkCorrectly()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var messageRepository = _serviceProvider.GetRequiredService<IMessageRepository>();
            
            // 创建原始消息
            var originalMessageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 1,
                Content = "原始消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            await messageService.ProcessMessageAsync(originalMessageOption);

            // 创建回复消息
            var replyMessageOption = new MessageOption
            {
                ChatId = 123456789,
                MessageId = 2,
                Content = "回复消息",
                UserId = 111222333,
                ReplyTo = 987654321,
                DateTime = DateTime.Now
            };

            // Act - 处理回复消息
            await messageService.ProcessMessageAsync(replyMessageOption);

            // Assert
            var replyMessage = await messageRepository.GetByIdAsync(
                new MessageId(replyMessageOption.ChatId, replyMessageOption.MessageId));
            
            Assert.NotNull(replyMessage);
            Assert.True(replyMessage.Metadata.HasReply);
            Assert.Equal(987654321, replyMessage.Metadata.ReplyToUserId);

            // 验证群组消息列表包含两条消息
            var groupMessages = await messageService.GetGroupMessagesAsync(replyMessageOption.ChatId);
            Assert.Equal(2, groupMessages.Count());
        }

        [Fact]
        public async Task BulkMessageProcessing_ShouldHandleMultipleMessages()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var messageRepository = _serviceProvider.GetRequiredService<IMessageRepository>();
            
            long groupId = 123456789;
            int messageCount = 10;

            // Act - 批量创建消息
            var messageIds = new List<long>();
            for (int i = 0; i < messageCount; i++)
            {
                var messageOption = new MessageOption
                {
                    ChatId = groupId,
                    MessageId = i + 1,
                    Content = $"批量消息 {i + 1}",
                    UserId = 987654321,
                    DateTime = DateTime.Now.AddMinutes(-i)
                };

                var processedId = await messageService.ProcessMessageAsync(messageOption);
                messageIds.Add(processedId);
            }

            // Assert
            Assert.Equal(messageCount, messageIds.Count);

            // 验证所有消息都被创建
            var groupMessages = await messageService.GetGroupMessagesAsync(groupId);
            Assert.Equal(messageCount, groupMessages.Count());

            // 验证分页功能
            var page1Messages = await messageService.GetGroupMessagesAsync(groupId, 1, 5);
            var page2Messages = await messageService.GetGroupMessagesAsync(groupId, 2, 5);
            
            Assert.Equal(5, page1Messages.Count());
            Assert.Equal(5, page2Messages.Count());

            // 验证搜索功能
            var searchResults = await messageService.SearchMessagesAsync(groupId, "批量");
            Assert.Equal(messageCount, searchResults.Count());

            // 验证消息计数
            var messageCountResult = await messageRepository.CountByGroupIdAsync(groupId);
            Assert.Equal(messageCount, messageCountResult);
        }

        [Fact]
        public async Task ErrorHandling_ShouldWorkCorrectly()
        {
            // Arrange
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            
            // 测试无效输入
            var invalidMessageOption = new MessageOption
            {
                ChatId = -1, // 无效的ChatId
                MessageId = 1,
                Content = "测试消息",
                UserId = 987654321,
                DateTime = DateTime.Now
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                messageService.ProcessMessageAsync(invalidMessageOption));

            // 测试空输入
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                messageService.ProcessMessageAsync(null));

            // 测试无效的查询参数
            await Assert.ThrowsAsync<ArgumentException>(() => 
                messageService.GetGroupMessagesAsync(-1, 1, 50));

            await Assert.ThrowsAsync<ArgumentException>(() => 
                messageService.SearchMessagesAsync(123456789, "", 1, 50));

            await Assert.ThrowsAsync<ArgumentException>(() => 
                messageService.GetUserMessagesAsync(123456789, -1, 1, 50));
        }
    }

    /// <summary>
    /// 内存中的消息仓储实现，用于集成测试
    /// </summary>
    public class InMemoryMessageRepository : IMessageRepository
    {
        private readonly Dictionary<(long ChatId, long MessageId), MessageAggregate> _messages = new();

        public Task<MessageAggregate> GetByIdAsync(MessageId id, System.Threading.CancellationToken cancellationToken = default)
        {
            var key = (id.ChatId, id.TelegramMessageId);
            _messages.TryGetValue(key, out var message);
            return Task.FromResult(message);
        }

        public Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, System.Threading.CancellationToken cancellationToken = default)
        {
            var messages = _messages.Values
                .Where(m => m.Id.ChatId == groupId)
                .OrderByDescending(m => m.Metadata.Timestamp);
            return Task.FromResult(messages);
        }

        public Task<MessageAggregate> AddAsync(MessageAggregate aggregate, System.Threading.CancellationToken cancellationToken = default)
        {
            var key = (aggregate.Id.ChatId, aggregate.Id.TelegramMessageId);
            _messages[key] = aggregate;
            return Task.FromResult(aggregate);
        }

        public Task UpdateAsync(MessageAggregate aggregate, System.Threading.CancellationToken cancellationToken = default)
        {
            var key = (aggregate.Id.ChatId, aggregate.Id.TelegramMessageId);
            _messages[key] = aggregate;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(MessageId id, System.Threading.CancellationToken cancellationToken = default)
        {
            var key = (id.ChatId, id.TelegramMessageId);
            _messages.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(MessageId id, System.Threading.CancellationToken cancellationToken = default)
        {
            var key = (id.ChatId, id.TelegramMessageId);
            return Task.FromResult(_messages.ContainsKey(key));
        }

        public Task<int> CountByGroupIdAsync(long groupId, System.Threading.CancellationToken cancellationToken = default)
        {
            var count = _messages.Values.Count(m => m.Id.ChatId == groupId);
            return Task.FromResult(count);
        }

        public Task<IEnumerable<MessageAggregate>> SearchAsync(long groupId, string query, int limit = 50, System.Threading.CancellationToken cancellationToken = default)
        {
            var messages = _messages.Values
                .Where(m => m.Id.ChatId == groupId && m.ContainsText(query))
                .OrderByDescending(m => m.Metadata.Timestamp)
                .Take(limit);
            return Task.FromResult(messages);
        }
    }
}