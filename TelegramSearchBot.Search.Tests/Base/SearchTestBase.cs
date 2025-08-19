using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Search.Manager;
using FluentAssertions;
using Xunit.Abstractions;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Base
{
    /// <summary>
    /// 搜索测试基类
    /// 提供搜索测试的基础设施和通用方法
    /// </summary>
    public abstract class SearchTestBase : IDisposable
    {
        protected readonly ITestOutputHelper Output;
        protected readonly string TestIndexRoot;
        protected readonly string LuceneIndexRoot;
        protected readonly string VectorIndexRoot;
        protected readonly ILogger<SearchTestBase> Logger;
        protected readonly DataDbContext TestDbContext;
        protected readonly IServiceProvider ServiceProvider;

        protected SearchTestBase(ITestOutputHelper output)
        {
            Output = output;
            
            // 创建测试目录
            TestIndexRoot = Path.Combine(Path.GetTempPath(), $"TelegramSearchBot_Test_{Guid.NewGuid()}");
            LuceneIndexRoot = Path.Combine(TestIndexRoot, "Lucene");
            VectorIndexRoot = Path.Combine(TestIndexRoot, "Vector");
            
            Directory.CreateDirectory(LuceneIndexRoot);
            Directory.CreateDirectory(VectorIndexRoot);
            
            Output.WriteLine($"Test index root: {TestIndexRoot}");

            // 配置服务
            var services = new ServiceCollection();
            
            // 添加数据库
            var dbContextOptions = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: $"SearchTestDb_{Guid.NewGuid()}")
                .Options;
            TestDbContext = new DataDbContext(dbContextOptions);
            services.AddSingleton(TestDbContext);
            
            // 添加日志
            services.AddLogging(builder => 
            {
                builder.AddProvider(new XunitLoggerProvider(output));
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            
            // 添加搜索服务
            services.AddSingleton<ILuceneManager>(new SearchLuceneManager(LuceneIndexRoot));
            services.AddSingleton<ISearchService, SearchService>();
            
            ServiceProvider = services.BuildServiceProvider();
            Logger = ServiceProvider.GetRequiredService<ILogger<SearchTestBase>>();
            
            // 初始化测试数据
            InitializeTestData().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 初始化测试数据
        /// </summary>
        protected virtual async Task InitializeTestData()
        {
            var testMessages = new List<Message>
            {
                new Message
                {
                    GroupId = 100,
                    MessageId = 1000,
                    FromUserId = 1,
                    Content = "Hello World! This is a test message.",
                    DateTime = DateTime.UtcNow.AddHours(-1)
                },
                new Message
                {
                    GroupId = 100,
                    MessageId = 1001,
                    FromUserId = 2,
                    Content = "Searching is fun! Let's test Lucene.",
                    DateTime = DateTime.UtcNow.AddMinutes(-30)
                },
                new Message
                {
                    GroupId = 100,
                    MessageId = 1002,
                    FromUserId = 1,
                    Content = "Vector search with FAISS is powerful.",
                    DateTime = DateTime.UtcNow.AddMinutes(-15)
                },
                new Message
                {
                    GroupId = 200,
                    MessageId = 2000,
                    FromUserId = 3,
                    Content = "This is a different group for testing.",
                    DateTime = DateTime.UtcNow.AddMinutes(-45)
                },
                new Message
                {
                    GroupId = 200,
                    MessageId = 2001,
                    FromUserId = 2,
                    Content = "Cross-group search functionality test.",
                    DateTime = DateTime.UtcNow.AddMinutes(-20)
                }
            };

            TestDbContext.Messages.AddRange(testMessages);
            await TestDbContext.SaveChangesAsync();
            
            Output.WriteLine($"Initialized {testMessages.Count} test messages");
        }

        /// <summary>
        /// 创建Lucene管理器实例
        /// </summary>
        protected ILuceneManager CreateLuceneManager(string? customIndexRoot = null)
        {
            var indexRoot = customIndexRoot ?? LuceneIndexRoot;
            return new SearchLuceneManager(indexRoot);
        }

        /// <summary>
        /// 创建搜索服务实例
        /// </summary>
        protected ISearchService CreateSearchService(ILuceneManager? luceneManager = null)
        {
            return new SearchService(
                TestDbContext,
                luceneManager ?? ServiceProvider.GetRequiredService<ILuceneManager>());
        }

        /// <summary>
        /// 创建测试消息
        /// </summary>
        protected Message CreateTestMessage(long groupId, long messageId, long fromUserId, string content)
        {
            return new Message
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = fromUserId,
                Content = content,
                DateTime = DateTime.UtcNow,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建大量测试消息用于性能测试
        /// </summary>
        protected List<Message> CreateBulkTestMessages(int count, long groupId = 100)
        {
            var messages = new List<Message>();
            var baseTime = DateTime.UtcNow.AddHours(-24);
            
            for (int i = 0; i < count; i++)
            {
                messages.Add(new Message
                {
                    GroupId = groupId,
                    MessageId = groupId * 10000 + i,
                    FromUserId = (i % 10) + 1,
                    Content = $"Test message {i} with content about search functionality. " +
                             $"This message contains keywords like 'search', 'test', 'lucene', 'vector', 'faiss'. " +
                             $"Random number: {new Random().Next(1, 1000)}",
                    DateTime = baseTime.AddMinutes(i)
                });
            }
            
            return messages;
        }

        /// <summary>
        /// 验证搜索结果
        /// </summary>
        protected void ValidateSearchResults(List<Message> results, int expectedCount, string expectedKeyword)
        {
            results.Should().NotBeNull();
            results.Should().HaveCount(expectedCount);
            
            foreach (var message in results)
            {
                message.Content.ToLower().Should().Contain(expectedKeyword.ToLower());
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                // 清理测试目录
                if (Directory.Exists(TestIndexRoot))
                {
                    Directory.Delete(TestIndexRoot, true);
                    Output.WriteLine($"Cleaned up test directory: {TestIndexRoot}");
                }
                
                // 清理数据库
                TestDbContext.Database.EnsureDeleted();
                TestDbContext.Dispose();
                
                // 清理服务提供者
                if (ServiceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}