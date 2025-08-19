using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Search.Manager;
using TelegramSearchBot.Infrastructure.Persistence.Repositories;
using TelegramSearchBot.Test.Helpers;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Test.Performance
{
    /// <summary>
    /// 搜索性能测试
    /// 
    /// 原本实现：只测试简单的字符串匹配
    /// 简化实现：使用真实的Lucene.NET索引和搜索功能
    /// 
    /// 限制：仍在内存中运行，没有测试磁盘I/O
    /// </summary>
    [Config(typeof(Config))]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [MemoryDiagnoser]
    public class SearchPerformanceBenchmarks
    {
        private class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithWarmupCount(2).WithIterationCount(3));
            }
        }

        private ILuceneManager _luceneManager;
        private TelegramSearchBot.Infrastructure.Persistence.Repositories.MessageRepository _messageRepository;
        private DataDbContext _context;
        private List<TelegramSearchBot.Model.Data.Message> _indexedMessages;

        [Params(1000, 10000, 50000)]
        public int IndexSize { get; set; }

        [GlobalSetup]
        public async Task Setup()
        {
            // 初始化数据库
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"SearchPerfTest_{Guid.NewGuid()}")
                .Options;
            _context = new DataDbContext(options);
            _messageRepository = new TelegramSearchBot.Infrastructure.Persistence.Repositories.MessageRepository(_context);
            
            // 初始化Lucene索引
            var indexPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"LuceneIndex_{Guid.NewGuid()}");
            _luceneManager = new SearchLuceneManager(indexPath);
            
            // 生成并索引测试数据
            _indexedMessages = GenerateSearchableMessages(IndexSize);
            
            // 添加到数据库
            await _context.Messages.AddRangeAsync(_indexedMessages);
            await _context.SaveChangesAsync();
            
            // 构建Lucene索引
            foreach (var message in _indexedMessages)
            {
                _luceneManager.WriteDocumentAsync(message).GetAwaiter().GetResult();
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _context?.Dispose();
        }

        [Benchmark]
        public async Task SimpleKeywordSearch()
        {
            var results = await _luceneManager.Search("测试", 1, 0, 50);
        }

        [Benchmark]
        public async Task PhraseSearch()
        {
            var results = await _luceneManager.SyntaxSearch("\"测试消息\"", 1, 0, 50);
        }

        [Benchmark]
        public async Task BooleanSearch()
        {
            var results = await _luceneManager.SyntaxSearch("测试 AND 消息", 1, 0, 50);
        }

        [Benchmark]
        public async Task WildcardSearch()
        {
            var results = await _luceneManager.SyntaxSearch("测试*", 1, 0, 50);
        }

        [Benchmark]
        public async Task FuzzySearch()
        {
            var results = await _luceneManager.SyntaxSearch("测试~", 1, 0, 50);
        }

        [Benchmark]
        public async Task SearchWithDateRange()
        {
            // 使用搜索功能模拟日期范围搜索
            var results = await _luceneManager.Search("测试", 1, 0, 100);
        }

        [Benchmark]
        public async Task SearchWithChatFilter()
        {
            var results = await _luceneManager.Search("消息", 1, 0, 50);
        }

        [Benchmark]
        public async Task DatabaseSearch()
        {
            // 对比数据库搜索性能
            var results = await _messageRepository.SearchMessagesAsync(1, "测试", 50);
        }

        [Benchmark]
        public async Task HighFrequencySearch()
        {
            // 模拟高频搜索场景
            for (int i = 0; i < 100; i++)
            {
                var results = await _luceneManager.Search($"query{i % 10}", 1, 0, 10);
            }
        }

        [Benchmark]
        public async Task ConcurrentSearches()
        {
            // 并发搜索测试
            var tasks = new List<System.Threading.Tasks.Task>();
            var queries = new[] { "测试", "消息", "内容", "搜索", "性能" };
            
            for (int i = 0; i < 50; i++)
            {
                var query = queries[i % queries.Length];
                tasks.Add(System.Threading.Tasks.Task.Run(async () => 
                {
                    var results = await _luceneManager.Search(query, 1, 0, 20);
                }));
            }
            
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        [Benchmark]
        public async Task ComplexQuerySearch()
        {
            // 复杂查询测试
            var complexQuery = "(测试 OR 消息) AND 内容 NOT (删除 OR 无效)";
            var results = await _luceneManager.SyntaxSearch(complexQuery, 1, 0, 100);
        }

        [Benchmark]
        public async Task PaginationSearch()
        {
            // 分页搜索测试
            for (int page = 0; page < 10; page++)
            {
                var results = await _luceneManager.Search("测试", 1, page * 20, 20);
            }
        }

        /// <summary>
        /// 生成适合搜索的测试消息
        /// 原本实现：使用简单的随机内容
        /// 简化实现：生成包含可搜索关键词的真实消息内容
        /// </summary>
        private List<Message> GenerateSearchableMessages(int count)
        {
            var messages = new List<Message>();
            var random = new Random(42);
            
            // 定义搜索关键词和它们的频率
            var keywords = new[]
            {
                ("测试", 0.3),
                ("消息", 0.25),
                ("内容", 0.2),
                ("搜索", 0.15),
                ("性能", 0.1),
                ("优化", 0.08),
                ("数据库", 0.05),
                ("索引", 0.05),
                ("Lucene", 0.03),
                ("向量", 0.02)
            };
            
            var templates = new[]
            {
                "这是一条关于{0}的{1}",
                "请帮我{0}相关的{1}",
                "我想知道{0}如何影响{1}",
                "{0}和{1}有什么关系",
                "为什么{0}这么重要",
                "如何提高{0}的{1}",
                "{0}的最佳实践是什么",
                "请解释{0}的概念"
            };
            
            for (int i = 0; i < count; i++)
            {
                // 根据频率随机选择关键词
                var selectedKeywords = new List<string>();
                foreach (var (keyword, frequency) in keywords)
                {
                    if (random.NextDouble() < frequency)
                    {
                        selectedKeywords.Add(keyword);
                    }
                }
                
                // 确保至少有一个关键词
                if (selectedKeywords.Count == 0)
                {
                    selectedKeywords.Add(keywords[0].Item1);
                }
                
                // 生成消息内容
                var template = templates[random.Next(templates.Length)];
                var content = string.Format(template, 
                    selectedKeywords[random.Next(selectedKeywords.Count)],
                    selectedKeywords.Count > 1 ? selectedKeywords[1] : "内容");
                
                // 添加一些随机文本
                content += $" 这是第{i+1}条消息，创建于{DateTime.UtcNow.AddDays(-random.Next(0, 365)):yyyy-MM-dd}";
                
                var message = TestDataFactory.CreateValidMessage(
                    groupId: 1 + (i % 5), // 5个不同的群组
                    messageId: i + 1,
                    fromUserId: 1 + (i % 20), // 20个不同的用户
                    content: content);
                
                // 设置不同的时间
                message.DateTime = DateTime.UtcNow.AddDays(-random.Next(0, 365))
                    .AddHours(-random.Next(0, 24));
                
                messages.Add(message);
            }
            
            return messages;
        }
    }
}