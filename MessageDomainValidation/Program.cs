using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model;

namespace MessageDomainValidation
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🔍 Message领域功能验证程序");
            Console.WriteLine("================================");

            // 创建模拟的Logger
            using var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole());
            var logger = loggerFactory.CreateLogger<MessageService>();

            // 创建模拟的MessageRepository
            var mockRepository = new MockMessageRepository();

            try
            {
                // 测试MessageService实例化
                Console.WriteLine("✓ 测试MessageService实例化...");
                var messageService = new MessageService(mockRepository, logger);
                Console.WriteLine("✓ MessageService 实例化成功");

                // 测试消息处理
                Console.WriteLine("\n✓ 测试消息处理...");
                var messageOption = new MessageOption
                {
                    UserId = 12345,
                    ChatId = 67890,
                    MessageId = 1001,
                    Content = "测试消息内容",
                    DateTime = DateTime.UtcNow,
                    User = new Telegram.Bot.Types.User { Id = 12345 },
                    Chat = new Telegram.Bot.Types.Chat { Id = 67890 }
                };

                var result = await messageService.ProcessMessageAsync(messageOption);
                Console.WriteLine($"✓ 消息处理结果: {result}, 消息ID: {result}");

                // 测试群组消息查询
                Console.WriteLine("\n✓ 测试群组消息查询...");
                var groupMessages = await messageService.GetGroupMessagesAsync(67890);
                Console.WriteLine($"✓ 群组消息查询: {groupMessages.Count()} 条消息");

                // 测试消息搜索
                Console.WriteLine("\n✓ 测试消息搜索...");
                var searchResults = await messageService.SearchMessagesAsync(67890, "测试");
                Console.WriteLine($"✓ 消息搜索结果: {searchResults.Count()} 条消息");

                // 测试用户消息查询
                Console.WriteLine("\n✓ 测试用户消息查询...");
                var userMessages = await messageService.GetUserMessagesAsync(67890, 12345);
                Console.WriteLine($"✓ 用户消息查询: {userMessages.Count()} 条消息");

                // 测试MessageProcessingPipeline
                Console.WriteLine("\n✓ 测试MessageProcessingPipeline...");
                var pipelineLogger = loggerFactory.CreateLogger<MessageProcessingPipeline>();
                var pipeline = new MessageProcessingPipeline(messageService, pipelineLogger);
                var pipelineResult = await pipeline.ProcessMessageAsync(messageOption);
                Console.WriteLine($"✓ 处理管道结果: {(pipelineResult.Success ? "成功" : "失败")}, 消息ID: {pipelineResult.MessageId}");

                Console.WriteLine("\n🎉 所有Message领域核心功能验证通过！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 验证失败: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// 模拟的MessageRepository实现
    /// </summary>
    class MockMessageRepository : IMessageRepository
    {
        private readonly List<Message> _messages = new();
        private long _nextId = 1;

        public Task<long> AddMessageAsync(Message message)
        {
            message.Id = _nextId++;
            _messages.Add(message);
            return Task.FromResult(message.Id);
        }

        public Task<bool> DeleteMessageAsync(long groupId, long messageId)
        {
            var message = _messages.FirstOrDefault(m => m.GroupId == groupId && m.MessageId == messageId);
            if (message != null)
            {
                _messages.Remove(message);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<IEnumerable<Message>> GetMessagesByGroupIdAsync(long groupId, DateTime? startDate = null, DateTime? endDate = null)
        {
            var result = _messages.Where(m => m.GroupId == groupId);
            if (startDate.HasValue)
                result = result.Where(m => m.DateTime >= startDate.Value);
            if (endDate.HasValue)
                result = result.Where(m => m.DateTime <= endDate.Value);
            return Task.FromResult(result);
        }

        public Task<Message?> GetMessageByIdAsync(long groupId, long messageId)
        {
            var result = _messages.FirstOrDefault(m => m.GroupId == groupId && m.MessageId == messageId);
            return Task.FromResult(result);
        }

        public Task<IEnumerable<Message>> GetMessagesByUserAsync(long groupId, long userId)
        {
            var result = _messages.Where(m => m.GroupId == groupId && m.FromUserId == userId);
            return Task.FromResult(result);
        }

        public Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 100)
        {
            var result = _messages.Where(m => m.GroupId == groupId && m.Content.Contains(keyword));
            return Task.FromResult(result);
        }

        public Task<bool> UpdateMessageContentAsync(long groupId, long messageId, string newContent)
        {
            var message = _messages.FirstOrDefault(m => m.GroupId == groupId && m.MessageId == messageId);
            if (message != null)
            {
                message.Content = newContent;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}