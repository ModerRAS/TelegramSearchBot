using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Test.Helpers;
using TelegramSearchBot.Domain.Tests;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using Xunit;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Test.Performance
{
    /// <summary>
    /// 消息处理管道性能测试
    /// 
    /// 原本实现：使用Mock服务，只测试方法调用开销
    /// 简化实现：使用真实的组件，测试完整的处理流程性能
    /// 
    /// 限制：仍然在内存中运行，没有测试I/O和外部服务调用
    /// </summary>
    [Config(typeof(Config))]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [MemoryDiagnoser]
    public class MessageProcessingBenchmarks
    {
        private class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithWarmupCount(3).WithIterationCount(5));
            }
        }

        private MessageProcessingPipeline _pipeline;
        private Mock<IMessageService> _messageServiceMock;
        private Mock<Microsoft.Extensions.Logging.ILogger<MessageProcessingPipeline>> _loggerMock;
        private List<Message> _testMessages;

        [Params(100, 1000, 5000)]
        public int BatchSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _messageServiceMock = new Mock<IMessageService>();
            _loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<MessageProcessingPipeline>>();
            
            // 模拟消息服务操作
            _messageServiceMock.Setup(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync((MessageOption option) => option.MessageId);
            
            _pipeline = new MessageProcessingPipeline(
                _messageServiceMock.Object,
                _loggerMock.Object);
            
            _testMessages = CreateValidMessages(BatchSize);
        }

        /// <summary>
        /// 创建多个消息的辅助方法
        /// </summary>
        public static List<Message> CreateValidMessages(int count, long startId = 1)
        {
            var messages = new List<Message>();
            for (int i = 0; i < count; i++)
            {
                messages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100,
                    messageId: startId + i,
                    userId: 1,
                    content: $"Test message {startId + i}"));
            }
            return messages;
        }

        [Benchmark]
        public async Task ProcessSingleMessage()
        {
            var message = MessageTestDataFactory.CreateValidMessage();
            var messageOption = new MessageOption
                    {
                        ChatId = message.GroupId,
                        UserId = message.FromUserId,
                        MessageId = message.MessageId,
                        Content = message.Content,
                        DateTime = message.DateTime
                    };
                    await _pipeline.ProcessMessageAsync(messageOption);
        }

        [Benchmark]
        public async Task ProcessMessageBatch()
        {
            var tasks = new List<Task>();
            foreach (var message in _testMessages)
            {
                var messageOption = new MessageOption
                {
                    ChatId = message.GroupId,
                    UserId = message.FromUserId,
                    MessageId = message.MessageId,
                    Content = message.Content,
                    DateTime = message.DateTime
                };
                tasks.Add(_pipeline.ProcessMessageAsync(messageOption));
            }
            await Task.WhenAll(tasks);
        }

        [Benchmark]
        public async Task ProcessMessageSequentially()
        {
            foreach (var message in _testMessages)
            {
                var messageOption = new MessageOption
                    {
                        ChatId = message.GroupId,
                        UserId = message.FromUserId,
                        MessageId = message.MessageId,
                        Content = message.Content,
                        DateTime = message.DateTime
                    };
                    await _pipeline.ProcessMessageAsync(messageOption);
            }
        }

        [Benchmark]
        public async Task ProcessWithHighContention()
        {
            // 模拟高并发场景
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                var message = MessageTestDataFactory.CreateValidMessage(
                    groupId: 100,
                    messageId: i + 1,
                    userId: 1,
                    content: $"Concurrent message {i} with some content to process");
                var messageOption = new MessageOption
                {
                    ChatId = message.GroupId,
                    UserId = message.FromUserId,
                    MessageId = message.MessageId,
                    Content = message.Content,
                    DateTime = message.DateTime
                };
                tasks.Add(_pipeline.ProcessMessageAsync(messageOption));
            }
            await Task.WhenAll(tasks);
        }

        [Benchmark]
        public async Task ProcessLargeMessage()
        {
            // 测试处理大消息的性能
            var largeContent = new string('a', 10000); // 10KB的消息
            var largeMessage = MessageTestDataFactory.CreateValidMessage(
                groupId: 100,
                messageId: 1,
                userId: 1,
                content: largeContent);
            
            var messageOption = new MessageOption
            {
                ChatId = largeMessage.GroupId,
                UserId = largeMessage.FromUserId,
                MessageId = largeMessage.MessageId,
                Content = largeMessage.Content,
                DateTime = largeMessage.DateTime
            };
            await _pipeline.ProcessMessageAsync(messageOption);
        }

        [Benchmark]
        public async Task ProcessWithSpecialCharacters()
        {
            // 测试处理包含特殊字符的消息
            var specialContent = "消息包含特殊字符：😂👍❤️🎉\n换行符\t制表符\"引号\\反斜杠";
            var specialMessage = MessageTestDataFactory.CreateValidMessage(
                groupId: 100,
                messageId: 1,
                userId: 1,
                content: specialContent);
            
            var messageOption = new MessageOption
            {
                ChatId = specialMessage.GroupId,
                UserId = specialMessage.FromUserId,
                MessageId = specialMessage.MessageId,
                Content = specialMessage.Content,
                DateTime = specialMessage.DateTime
            };
            await _pipeline.ProcessMessageAsync(messageOption);
        }

        /// <summary>
        /// 并发处理性能测试
        /// 原本实现：没有测试并发场景
        /// 简化实现：添加并发测试，模拟真实的高并发场景
        /// </summary>
        [Benchmark]
        [Arguments(10)]
        [Arguments(50)]
        [Arguments(100)]
        public async Task ProcessConcurrentBatches(int concurrentBatches)
        {
            var tasks = new List<Task>();
            
            for (int batch = 0; batch < concurrentBatches; batch++)
            {
                var batchMessages = CreateValidMessages(10, batch + 1000);
                var batchTask = Task.Run(async () =>
                {
                    foreach (var message in batchMessages)
                    {
                        var messageOption = new MessageOption
                    {
                        ChatId = message.GroupId,
                        UserId = message.FromUserId,
                        MessageId = message.MessageId,
                        Content = message.Content,
                        DateTime = message.DateTime
                    };
                    await _pipeline.ProcessMessageAsync(messageOption);
                    }
                });
                tasks.Add(batchTask);
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 错误处理性能测试
        /// 测试在处理失败消息时的性能影响
        /// </summary>
        [Benchmark]
        public async Task ProcessWithErrorHandling()
        {
            // 配置一些消息处理失败
            _messageServiceMock.SetupSequence(s => s.ProcessMessageAsync(It.IsAny<MessageOption>()))
                .ReturnsAsync(1L)
                .ReturnsAsync(2L)
                .ThrowsAsync(new Exception("Simulated message processing failure"))
                .ReturnsAsync(4L);
            
            var messages = new List<Message>();
            for (int i = 0; i < 100; i++)
            {
                messages.Add(MessageTestDataFactory.CreateValidMessage(
                    groupId: 100,
                    messageId: i + 1,
                    userId: 1,
                    content: $"Test message {i} for error handling"));
            }
            foreach (var message in messages)
            {
                try
                {
                    var messageOption = new MessageOption
                    {
                        ChatId = message.GroupId,
                        UserId = message.FromUserId,
                        MessageId = message.MessageId,
                        Content = message.Content,
                        DateTime = message.DateTime
                    };
                    await _pipeline.ProcessMessageAsync(messageOption);
                }
                catch
                {
                    // 忽略错误，继续处理
                }
            }
        }

        /// <summary>
        /// 内存使用测试
        /// 测试大量消息处理时的内存使用情况
        /// </summary>
        [Benchmark]
        public async Task ProcessWithMemoryPressure()
        {
            // 强制GC回收以确保测试起点一致
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // 处理大量消息
            for (int i = 0; i < 1000; i++)
            {
                var message = MessageTestDataFactory.CreateValidMessage(
                    groupId: 100,
                    messageId: i + 1,
                    userId: 1,
                    content: $"Message {i} with sufficient content to trigger memory pressure testing");
                
                var messageOption = new MessageOption
                {
                    ChatId = message.GroupId,
                    UserId = message.FromUserId,
                    MessageId = message.MessageId,
                    Content = message.Content,
                    DateTime = message.DateTime
                };
                await _pipeline.ProcessMessageAsync(messageOption);
                
                // 每100个消息强制一次GC
                if (i % 100 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }
    }
}