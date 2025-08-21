using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Test.Helpers;

namespace TelegramSearchBot.Benchmarks.Domain.Message
{
    /// <summary>
    /// MessageProcessingPipeline 性能基准测试
    /// 测试消息处理管道在不同负载下的性能表现
    /// </summary>
    [Config(typeof(MessageProcessingBenchmarkConfig))]
    [MemoryDiagnoser]
    public class MessageProcessingBenchmarks
    {
        private class MessageProcessingBenchmarkConfig : ManualConfig
        {
            public MessageProcessingBenchmarkConfig()
            {
                AddColumn(StatisticColumn.AllStatistics);
                AddDiagnoser(MemoryDiagnoser.Default);
                AddJob(Job.Default.WithIterationCount(10).WithWarmupCount(3));
            }
        }

        private readonly Mock<IMessageService> _mockMessageService;
        private readonly Mock<ILogger<MessageProcessingPipeline>> _mockLogger;
        private MessageProcessingPipeline _pipeline;
        private readonly Consumer _consumer = new Consumer();

        // 测试数据集
        private List<MessageOption> _smallBatch;
        private List<MessageOption> _mediumBatch;
        private List<MessageOption> _largeBatch;
        private MessageOption _singleMessage;
        private MessageOption _longMessage;
        private MessageOption _messageWithExtensions;

        public MessageProcessingBenchmarks()
        {
            _mockMessageService = new Mock<IMessageService>();
            _mockLogger = new Mock<ILogger<MessageProcessingPipeline>>();
            
            // 初始化测试数据
            InitializeTestData();
            
            // 设置模拟服务返回值
            SetupMockService();
        }

        [GlobalSetup]
        public void Setup()
        {
            _pipeline = new MessageProcessingPipeline(_mockMessageService.Object, _mockLogger.Object);
        }

        /// <summary>
        /// 初始化测试数据
        /// </summary>
        private void InitializeTestData()
        {
            var random = new Random(42);
            
            // 单条消息
            _singleMessage = TestDataFactory.CreateValidMessageOption(
                userId: 1,
                chatId: 100,
                messageId: 1000,
                content: "Simple test message"
            );

            // 长消息
            _longMessage = TestDataFactory.CreateLongMessage(
                userId: 2,
                chatId: 100,
                wordCount: 500
            );

            // 带扩展的消息
            _messageWithExtensions = TestDataFactory.CreateValidMessageOption(
                userId: 3,
                chatId: 100,
                messageId: 1001,
                content: "Message with extensions"
            );

            // 小批量消息：10条
            _smallBatch = Enumerable.Range(0, 10)
                .Select(i => TestDataFactory.CreateValidMessageOption(
                    userId: i + 1,
                    chatId: 100,
                    messageId: 2000 + i,
                    content: $"Small batch message {i}"
                ))
                .ToList();

            // 中批量消息：100条
            _mediumBatch = Enumerable.Range(0, 100)
                .Select(i => TestDataFactory.CreateValidMessageOption(
                    userId: (i % 20) + 1, // 20个不同用户
                    chatId: 100,
                    messageId: 3000 + i,
                    content: $"Medium batch message {i} with some additional content"
                ))
                .ToList();

            // 大批量消息：1000条
            _largeBatch = Enumerable.Range(0, 1000)
                .Select(i => TestDataFactory.CreateValidMessageOption(
                    userId: (i % 50) + 1, // 50个不同用户
                    chatId: 100,
                    messageId: 4000 + i,
                    content: $"Large batch message {i} with substantial content for testing performance under load"
                ))
                .ToList();

            // 为部分消息添加回复关系
            foreach (var batch in new[] { _smallBatch, _mediumBatch, _largeBatch })
            {
                for (int i = 1; i < batch.Count; i++)
                {
                    if (random.NextDouble() < 0.1) // 10%的消息是回复
                    {
                        batch[i].ReplyTo = batch[i - 1].MessageId;
                    }
                }
            }
        }

        /// <summary>
        /// 设置模拟服务的返回值
        /// </summary>
        private void SetupMockService()
        {
            // 模拟消息处理成功
            _mockMessageService
                .Setup(service => service.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption option) => option.MessageId);

            // 模拟处理延迟（可选）
            _mockMessageService
                .Setup(service => service.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption option) => 
                {
                    // 模拟处理时间
                    Task.Delay(1).Wait();
                    return option.MessageId;
                });
        }

        #region 单条消息处理性能测试

        /// <summary>
        /// 测试单条简单消息处理性能 - 基准测试
        /// </summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("SingleMessage")]
        public async Task ProcessSingleMessage()
        {
            var result = await _pipeline.ProcessMessageAsync(_singleMessage);
        }

        /// <summary>
        /// 测试长消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SingleMessage")]
        public async Task ProcessLongMessage()
        {
            var result = await _pipeline.ProcessMessageAsync(_longMessage);
        }

        /// <summary>
        /// 测试带扩展的消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SingleMessage")]
        public async Task ProcessMessageWithExtensions()
        {
            var result = await _pipeline.ProcessMessageAsync(_messageWithExtensions);
        }

        /// <summary>
        /// 测试带回复的消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SingleMessage")]
        public async Task ProcessMessageWithReply()
        {
            var replyMessage = TestDataFactory.CreateMessageWithReply(
                userId: 4,
                chatId: 100,
                messageId: 1002,
                replyToMessageId: 1000
            );
            var result = await _pipeline.ProcessMessageAsync(replyMessage);
        }

        #endregion

        #region 批量消息处理性能测试

        /// <summary>
        /// 测试小批量消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BatchProcessing")]
        public async Task ProcessSmallBatch()
        {
            var result = await _pipeline.ProcessMessagesAsync(_smallBatch);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试中批量消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BatchProcessing")]
        public async Task ProcessMediumBatch()
        {
            var result = await _pipeline.ProcessMessagesAsync(_mediumBatch);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试大批量消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BatchProcessing")]
        public async Task ProcessLargeBatch()
        {
            var result = await _pipeline.ProcessMessagesAsync(_largeBatch);
            result.Consume(_consumer);
        }

        #endregion

        #region 不同内容类型性能测试

        /// <summary>
        /// 测试中文消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("ContentType")]
        public async Task ProcessChineseMessage()
        {
            var chineseMessage = TestDataFactory.CreateValidMessageOption(
                userId: 5,
                chatId: 100,
                messageId: 1003,
                content: "这是一条中文测试消息，包含各种中文字符和标点符号，用于测试中文内容的处理性能。"
            );
            var result = await _pipeline.ProcessMessageAsync(chineseMessage);
        }

        /// <summary>
        /// 测试包含特殊字符的消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("ContentType")]
        public async Task ProcessSpecialCharsMessage()
        {
            var specialMessage = TestDataFactory.CreateMessageWithSpecialChars(
                userId: 6,
                chatId: 100
            );
            var result = await _pipeline.ProcessMessageAsync(specialMessage);
        }

        /// <summary>
        /// 测试包含Emoji的消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("ContentType")]
        public async Task ProcessEmojiMessage()
        {
            var emojiMessage = TestDataFactory.CreateValidMessageOption(
                userId: 7,
                chatId: 100,
                messageId: 1004,
                content: "Hello! 😊 How are you today? 🎉 Let's celebrate with some emojis! 🚀💯🔥"
            );
            var result = await _pipeline.ProcessMessageAsync(emojiMessage);
        }

        /// <summary>
        /// 测试包含代码的消息处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("ContentType")]
        public async Task ProcessCodeMessage()
        {
            var codeMessage = TestDataFactory.CreateValidMessageOption(
                userId: 8,
                chatId: 100,
                messageId: 1005,
                content: @"Here's some code:
```csharp
public async Task<IActionResult> GetMessage(int id)
{
    var message = await _service.GetByIdAsync(id);
    return Ok(message);
}
```
This demonstrates code block processing."
            );
            var result = await _pipeline.ProcessMessageAsync(codeMessage);
        }

        #endregion

        #region 并发处理性能测试

        /// <summary>
        /// 测试并发处理多条消息的性能 - 简化实现：使用Task.WhenAll模拟并发
        /// 注意：这不是真正的并发测试，只是模拟并发场景
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task ProcessConcurrentMessages()
        {
            var concurrentMessages = Enumerable.Range(0, 50)
                .Select(i => TestDataFactory.CreateValidMessageOption(
                    userId: (i % 10) + 1,
                    chatId: 100,
                    messageId: 5000 + i,
                    content: $"Concurrent message {i}"
                ))
                .ToList();

            // 模拟并发处理
            var tasks = concurrentMessages.Select(msg => _pipeline.ProcessMessageAsync(msg));
            var results = await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 测试混合负载下的处理性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task ProcessMixedWorkload()
        {
            var mixedMessages = new List<MessageOption>();
            
            // 添加不同类型的消息
            mixedMessages.AddRange(_smallBatch.Take(5)); // 简单消息
            mixedMessages.Add(_longMessage); // 长消息
            mixedMessages.Add(TestDataFactory.CreateMessageWithSpecialChars()); // 特殊字符
            mixedMessages.Add(TestDataFactory.CreateValidMessageOption(content: "中文测试消息")); // 中文消息

            var result = await _pipeline.ProcessMessagesAsync(mixedMessages);
            result.Consume(_consumer);
        }

        #endregion

        #region 内存分配测试

        /// <summary>
        /// 测试大量短消息的内存分配
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Memory")]
        public async Task ProcessManyShortMessages()
        {
            var shortMessages = Enumerable.Range(0, 100)
                .Select(i => TestDataFactory.CreateValidMessageOption(
                    userId: 1,
                    chatId: 100,
                    messageId: 6000 + i,
                    content: $"Short {i}"
                ))
                .ToList();

            var result = await _pipeline.ProcessMessagesAsync(shortMessages);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试处理后的内存占用
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Memory")]
        public async Task ProcessMemoryIntensiveMessages()
        {
            var memoryIntensiveMessages = Enumerable.Range(0, 20)
                .Select(i => TestDataFactory.CreateLongMessage(
                    userId: 1,
                    chatId: 100,
                    wordCount: 1000
                ))
                .ToList();

            var result = await _pipeline.ProcessMessagesAsync(memoryIntensiveMessages);
            result.Consume(_consumer);
        }

        #endregion
    }
}