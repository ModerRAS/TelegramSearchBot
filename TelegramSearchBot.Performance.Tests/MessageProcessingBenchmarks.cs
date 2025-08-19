using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Performance.Tests
{
    /// <summary>
    /// 消息处理的性能基准测试
    /// 测试DDD架构中关键路径的性能表现
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net90)]
    [HideColumns("Error", "StdDev", "Median", "RatioSD")]
    public class MessageProcessingBenchmarks
    {
        private readonly List<MessageAggregate> _testMessages;
        private readonly List<MessageOption> _testMessageOptions;

        public MessageProcessingBenchmarks()
        {
            _testMessages = CreateTestMessages(1000);
            _testMessageOptions = CreateTestMessageOptions(1000);
        }

        [Benchmark]
        public void MessageAggregateCreation()
        {
            for (int i = 0; i < 1000; i++)
            {
                var aggregate = MessageAggregate.Create(
                    chatId: 123456789,
                    messageId: i + 1,
                    content: $"测试消息 {i + 1}",
                    fromUserId: 987654321,
                    timestamp: DateTime.Now);
            }
        }

        [Benchmark]
        public void MessageAggregateWithReplyCreation()
        {
            for (int i = 0; i < 1000; i++)
            {
                var aggregate = MessageAggregate.Create(
                    chatId: 123456789,
                    messageId: i + 1,
                    content: $"回复消息 {i + 1}",
                    fromUserId: 987654321,
                    replyToUserId: 111222333,
                    replyToMessageId: i,
                    timestamp: DateTime.Now);
            }
        }

        [Benchmark]
        public void ValueObjectCreation()
        {
            for (int i = 0; i < 1000; i++)
            {
                var messageId = new MessageId(123456789, i + 1);
                var content = new MessageContent($"测试消息内容 {i + 1}");
                var metadata = new MessageMetadata(987654321, DateTime.Now);
            }
        }

        [Benchmark]
        public void MessageContentOperations()
        {
            var content = new MessageContent("这是一条测试消息内容，用于测试各种字符串操作的性能表现");
            
            for (int i = 0; i < 1000; i++)
            {
                var contains = content.Contains("测试");
                var startsWith = content.StartsWith("这是");
                var endsWith = content.EndsWith("表现");
                var substring = content.Substring(5, 10);
                var trimmed = content.Trim();
            }
        }

        [Benchmark]
        public void DomainEventCreation()
        {
            var messageId = new MessageId(123456789, 1);
            var content = new MessageContent("测试消息");
            var metadata = new MessageMetadata(987654321, DateTime.Now);

            for (int i = 0; i < 1000; i++)
            {
                var createdEvent = new MessageCreatedEvent(messageId, content, metadata);
                var updatedEvent = new MessageContentUpdatedEvent(messageId, content, new MessageContent("更新内容"));
                var replyEvent = new MessageReplyUpdatedEvent(messageId, 0, 0, 111222333, 1);
            }
        }

        [Benchmark]
        public void MessageAggregateOperations()
        {
            var aggregate = MessageAggregate.Create(
                chatId: 123456789,
                messageId: 1,
                content: "原始消息内容",
                fromUserId: 987654321,
                timestamp: DateTime.Now);

            for (int i = 0; i < 1000; i++)
            {
                var isFromUser = aggregate.IsFromUser(987654321);
                var containsText = aggregate.ContainsText("原始");
                var isRecent = aggregate.IsRecent;
                var age = aggregate.Age;
                
                // 更新内容
                var newContent = new MessageContent($"更新后的消息内容 {i}");
                aggregate.UpdateContent(newContent);
                
                // 更新回复
                aggregate.UpdateReply(111222333, 1);
                
                // 移除回复
                aggregate.RemoveReply();
            }
        }

        [Benchmark]
        public void EqualityOperations()
        {
            var messageId1 = new MessageId(123456789, 1);
            var messageId2 = new MessageId(123456789, 2);
            var content1 = new MessageContent("消息1");
            var content2 = new MessageContent("消息2");

            for (int i = 0; i < 1000; i++)
            {
                var equals1 = messageId1.Equals(messageId2);
                var equals2 = content1.Equals(content2);
                var hash1 = messageId1.GetHashCode();
                var hash2 = messageId2.GetHashCode();
                var opEquals = messageId1 == messageId2;
                var opNotEquals = messageId1 != messageId2;
            }
        }

        [Benchmark]
        public void MessageValidation()
        {
            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    // 测试无效的MessageId
                    var invalidMessageId = new MessageId(-1, 1);
                }
                catch { }

                try
                {
                    // 测试无效的MessageContent
                    var invalidContent = new MessageContent(null);
                }
                catch { }

                try
                {
                    // 测试过长的MessageContent
                    var longContent = new string('A', 5001);
                    var invalidContent = new MessageContent(longContent);
                }
                catch { }

                try
                {
                    // 测试无效的MessageMetadata
                    var invalidMetadata = new MessageMetadata(-1, DateTime.Now);
                }
                catch { }
            }
        }

        [Benchmark]
        public async Task LargeDataSetProcessing()
        {
            var largeMessageSet = CreateTestMessages(10000);
            
            // 模拟处理大量消息
            var tasks = new List<Task>();
            foreach (var message in largeMessageSet)
            {
                tasks.Add(Task.Run(() =>
                {
                    var isFromUser = message.IsFromUser(987654321);
                    var containsText = message.ContainsText("测试");
                    var isRecent = message.IsRecent;
                    var domainEvents = message.DomainEvents;
                }));
            }
            
            await Task.WhenAll(tasks);
        }

        [Benchmark]
        public void SearchOperations()
        {
            var messages = CreateTestMessages(1000);
            var searchTerm = "测试";
            
            for (int i = 0; i < 100; i++)
            {
                var results = messages.Where(m => m.ContainsText(searchTerm)).ToList();
                var userMessages = messages.Where(m => m.IsFromUser(987654321)).ToList();
                var recentMessages = messages.Where(m => m.IsRecent).ToList();
                var replyMessages = messages.Where(m => m.Metadata.HasReply).ToList();
            }
        }

        [Benchmark]
        public void MessageTransformation()
        {
            var aggregate = MessageAggregate.Create(
                chatId: 123456789,
                messageId: 1,
                content: "原始消息内容",
                fromUserId: 987654321,
                timestamp: DateTime.Now);

            for (int i = 0; i < 1000; i++)
            {
                // 内容转换
                var trimmed = aggregate.Content.Trim();
                var substring = aggregate.Content.Substring(0, Math.Min(10, aggregate.Content.Length));
                
                // ID转换
                var idString = aggregate.Id.ToString();
                
                // 元数据转换
                var metadataString = aggregate.Metadata.ToString();
                
                // 时间转换
                var age = aggregate.Age;
                var isRecent = aggregate.IsRecent;
            }
        }

        private List<MessageAggregate> CreateTestMessages(int count)
        {
            var messages = new List<MessageAggregate>();
            for (int i = 0; i < count; i++)
            {
                var aggregate = MessageAggregate.Create(
                    chatId: 123456789,
                    messageId: i + 1,
                    content: $"测试消息 {i + 1} 包含一些搜索关键词",
                    fromUserId: 987654321 + (i % 10),
                    timestamp: DateTime.Now.AddMinutes(-i));
                
                if (i % 5 == 0)
                {
                    // 每5条消息中有一条是回复
                    aggregate.UpdateReply(111222333, i);
                }
                
                messages.Add(aggregate);
            }
            return messages;
        }

        private List<MessageOption> CreateTestMessageOptions(int count)
        {
            var options = new List<MessageOption>();
            for (int i = 0; i < count; i++)
            {
                var option = new MessageOption
                {
                    ChatId = 123456789,
                    MessageId = i + 1,
                    Content = $"测试消息 {i + 1}",
                    UserId = 987654321 + (i % 10),
                    DateTime = DateTime.Now.AddMinutes(-i)
                };
                
                if (i % 5 == 0)
                {
                    option.ReplyTo = 111222333;
                }
                
                options.Add(option);
            }
            return options;
        }
    }

    /// <summary>
    /// 查询性能基准测试
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net90)]
    [HideColumns("Error", "StdDev", "Median", "RatioSD")]
    public class QueryPerformanceBenchmarks
    {
        private readonly List<MessageAggregate> _largeDataSet;
        private readonly List<MessageAggregate> _mediumDataSet;

        public QueryPerformanceBenchmarks()
        {
            _largeDataSet = CreateTestMessages(10000);
            _mediumDataSet = CreateTestMessages(1000);
        }

        [Benchmark]
        public void LargeDataSetSearch()
        {
            var results = _largeDataSet.Where(m => m.ContainsText("测试")).ToList();
        }

        [Benchmark]
        public void MediumDataSetSearch()
        {
            var results = _mediumDataSet.Where(m => m.ContainsText("测试")).ToList();
        }

        [Benchmark]
        public void UserFiltering()
        {
            var results = _largeDataSet.Where(m => m.IsFromUser(987654321)).ToList();
        }

        [Benchmark]
        public void RecentMessagesFiltering()
        {
            var results = _largeDataSet.Where(m => m.IsRecent).ToList();
        }

        [Benchmark]
        public void ReplyMessagesFiltering()
        {
            var results = _largeDataSet.Where(m => m.Metadata.HasReply).ToList();
        }

        [Benchmark]
        public void ComplexQuery()
        {
            var results = _largeDataSet
                .Where(m => m.ContainsText("测试") && m.IsFromUser(987654321) && m.IsRecent)
                .OrderByDescending(m => m.Metadata.Timestamp)
                .Take(100)
                .ToList();
        }

        [Benchmark]
        public void PaginationSimulation()
        {
            const int pageSize = 50;
            const int totalPages = 20;
            
            for (int page = 1; page <= totalPages; page++)
            {
                var results = _largeDataSet
                    .OrderByDescending(m => m.Metadata.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
        }

        private List<MessageAggregate> CreateTestMessages(int count)
        {
            var messages = new List<MessageAggregate>();
            for (int i = 0; i < count; i++)
            {
                var aggregate = MessageAggregate.Create(
                    chatId: 123456789,
                    messageId: i + 1,
                    content: $"测试消息 {i + 1} 包含一些搜索关键词和内容",
                    fromUserId: 987654321 + (i % 10),
                    timestamp: DateTime.Now.AddMinutes(-i % 1440)); // 24小时内
                
                if (i % 5 == 0)
                {
                    aggregate.UpdateReply(111222333, i);
                }
                
                messages.Add(aggregate);
            }
            return messages;
        }
    }
}