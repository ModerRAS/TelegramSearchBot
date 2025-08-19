using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Benchmarks.Quick
{
    /// <summary>
    /// 快速性能测试套件
    /// 用于快速验证系统性能状态，适合CI/CD和日常检查
    /// </summary>
    [Config(typeof(QuickBenchmarkConfig))]
    [MemoryDiagnoser]
    public class QuickPerformanceBenchmarks
    {
        private class QuickBenchmarkConfig : ManualConfig
        {
            public QuickBenchmarkConfig()
            {
                AddColumn(BenchmarkDotNet.Columns.StatisticColumn.AllStatistics);
                AddDiagnoser(MemoryDiagnoser.Default);
                AddJob(Job.ShortRun.WithIterationCount(3).WithWarmupCount(1));
            }
        }

        private readonly Consumer _consumer = new Consumer();
        
        // 简化的测试数据
        private readonly List<Message> _testMessages;
        private readonly List<MessageOption> _testMessageOptions;

        public QuickPerformanceBenchmarks()
        {
            // 生成少量测试数据
            _testMessages = GenerateTestMessages(100);
            _testMessageOptions = GenerateTestMessageOptions(100);
        }

        /// <summary>
        /// 生成测试消息
        /// </summary>
        private List<Message> GenerateTestMessages(int count)
        {
            var messages = new List<Message>();
            for (int i = 0; i < count; i++)
            {
                messages.Add(new Message
                {
                    Id = i + 1,
                    GroupId = 100,
                    MessageId = i + 1,
                    FromUserId = 1,
                    Content = $"Quick test message {i}",
                    DateTime = DateTime.UtcNow,
                    ReplyToUserId = 0,
                    ReplyToMessageId = 0,
                    MessageExtensions = new List<MessageExtension>()
                });
            }
            return messages;
        }

        /// <summary>
        /// 生成测试消息选项
        /// </summary>
        private List<MessageOption> GenerateTestMessageOptions(int count)
        {
            var options = new List<MessageOption>();
            for (int i = 0; i < count; i++)
            {
                options.Add(new MessageOption
                {
                    UserId = 1,
                    ChatId = 100,
                    MessageId = i + 1,
                    Content = $"Quick test message option {i}",
                    DateTime = DateTime.UtcNow,
                    ReplyTo = 0,
                    User = new User { Id = 1, FirstName = "Test" },
                    Chat = new Chat { Id = 100, Title = "Test Chat" }
                });
            }
            return options;
        }

        /// <summary>
        /// 测试消息创建性能
        /// </summary>
        [Benchmark(Baseline = true)]
        public void MessageCreation()
        {
            var message = new Message
            {
                GroupId = 100,
                MessageId = 1,
                FromUserId = 1,
                Content = "Test message",
                DateTime = DateTime.UtcNow,
                ReplyToUserId = 0,
                ReplyToMessageId = 0,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 测试列表查询性能
        /// </summary>
        [Benchmark]
        public void ListQuery()
        {
            var result = _testMessages.Where(m => m.GroupId == 100).Take(10).ToList();
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试字符串搜索性能
        /// </summary>
        [Benchmark]
        public void StringSearch()
        {
            var result = _testMessages.Where(m => m.Content.Contains("test")).ToList();
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试消息验证性能
        /// </summary>
        [Benchmark]
        public void MessageValidation()
        {
            var isValid = _testMessages.All(m => 
                m.GroupId > 0 && 
                m.MessageId > 0 && 
                !string.IsNullOrEmpty(m.Content));
        }

        /// <summary>
        /// 测试内存分配 - 创建大量对象
        /// </summary>
        [Benchmark]
        public void MemoryAllocation()
        {
            var messages = new List<Message>();
            for (int i = 0; i < 1000; i++)
            {
                messages.Add(new Message
                {
                    GroupId = 100,
                    MessageId = i,
                    FromUserId = 1,
                    Content = $"Memory test {i}",
                    DateTime = DateTime.UtcNow,
                    ReplyToUserId = 0,
                    ReplyToMessageId = 0,
                    MessageExtensions = new List<MessageExtension>()
                });
            }
        }

        /// <summary>
        /// 测试字典查找性能
        /// </summary>
        [Benchmark]
        public void DictionaryLookup()
        {
            var dict = _testMessages.ToDictionary(m => m.MessageId);
            var result = dict.TryGetValue(50, out var message);
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试排序性能
        /// </summary>
        [Benchmark]
        public void Sorting()
        {
            var result = _testMessages.OrderByDescending(m => m.DateTime).Take(50).ToList();
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试LINQ选择性能
        /// </summary>
        [Benchmark]
        public void LinqSelect()
        {
            var result = _testMessages.Select(m => new { m.MessageId, m.Content }).ToList();
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试日期时间处理性能
        /// </summary>
        [Benchmark]
        public void DateTimeProcessing()
        {
            var result = _testMessages
                .Where(m => m.DateTime > DateTime.UtcNow.AddDays(-1))
                .ToList();
            result.Consume(_consumer);
        }

        /// <summary>
        /// 测试字符串处理性能
        /// </summary>
        [Benchmark]
        public void StringProcessing()
        {
            var result = _testMessages
                .Select(m => m.Content.ToUpper())
                .ToList();
            result.Consume(_consumer);
        }
    }

    /// <summary>
    /// 快速性能测试程序
    /// 用于CI/CD流水线和快速性能检查
    /// </summary>
    public class QuickBenchmarkProgram
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("⚡ TelegramSearchBot 快速性能测试");
            Console.WriteLine("=================================");
            Console.WriteLine();

            try
            {
                Console.WriteLine("🔍 运行快速性能基准测试...");
                Console.WriteLine("📊 测试内容包括:");
                Console.WriteLine("  - 消息创建性能");
                Console.WriteLine("  - 查询操作性能");
                Console.WriteLine("  - 内存分配效率");
                Console.WriteLine("  - 字符串处理性能");
                Console.WriteLine("  - 数据结构操作性能");
                Console.WriteLine();

                // 运行快速基准测试
                BenchmarkDotNet.Running.BenchmarkRunner.Run<QuickPerformanceBenchmarks>();

                Console.WriteLine("✅ 快速性能测试完成!");
                Console.WriteLine();
                Console.WriteLine("💡 提示:");
                Console.WriteLine("  - 如果所有指标都在合理范围内，系统性能正常");
                Console.WriteLine("  - 如果发现性能问题，请运行完整的性能测试套件");
                Console.WriteLine("  - 建议定期运行此测试以监控性能变化");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 快速性能测试失败: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}