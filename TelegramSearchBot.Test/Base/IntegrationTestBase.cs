using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using TelegramSearchBot.AI.Interface.LLM;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Common.Interface.AI;
using TelegramSearchBot.Common.Interface.Vector;
using TelegramSearchBot.Common.Interface.Bilibili;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Test.Helpers;
using MediatR;

namespace TelegramSearchBot.Test.Base
{
    /// <summary>
    /// 集成测试基类，提供完整的测试基础设施
    /// </summary>
    public abstract class IntegrationTestBase : IDisposable
    {
        protected readonly IServiceProvider _serviceProvider;
        protected readonly DataDbContext _dbContext;
        protected readonly Mock<ITelegramBotClient> _botClientMock;
        protected readonly Mock<IGeneralLLMService> _llmServiceMock;
        protected readonly Mock<ILogger<IntegrationTestBase>> _loggerMock;
        protected readonly Mock<IMediator> _mediatorMock;
        protected readonly TestDataSet _testData;
        protected readonly IEnvService _envService;

        protected IntegrationTestBase()
        {
            // 创建服务集合
            var services = new ServiceCollection();

            // 配置测试服务
            ConfigureServices(services);

            // 构建服务提供者
            _serviceProvider = services.BuildServiceProvider();

            // 获取核心服务
            _dbContext = _serviceProvider.GetRequiredService<DataDbContext>();
            _botClientMock = _serviceProvider.GetRequiredService<Mock<ITelegramBotClient>>();
            _llmServiceMock = _serviceProvider.GetRequiredService<Mock<IGeneralLLMService>>();
            _loggerMock = _serviceProvider.GetRequiredService<Mock<ILogger<IntegrationTestBase>>>();
            _mediatorMock = _serviceProvider.GetRequiredService<Mock<IMediator>>();
            _envService = _serviceProvider.GetRequiredService<IEnvService>();

            // 创建测试数据
            _testData = TestDatabaseHelper.CreateStandardTestDataAsync(_dbContext).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 配置服务集合
        /// </summary>
        /// <param name="services">服务集合</param>
        protected virtual void ConfigureServices(IServiceCollection services)
        {
            // 配置数据库
            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            // 注册Mock服务
            services.AddSingleton(MockServiceFactory.CreateTelegramBotClientMock());
            services.AddSingleton(MockServiceFactory.CreateLLMServiceMock());
            services.AddSingleton(MockServiceFactory.CreateLoggerMock<IntegrationTestBase>());
            services.AddSingleton(MockServiceFactory.CreateMediatorMock());
            services.AddSingleton(MockServiceFactory.CreateSendMessageMock().Object);
            services.AddSingleton(MockServiceFactory.CreateLuceneManagerMock().Object);

            // 注册配置服务
            services.AddSingleton<IEnvService>(new TestEnvService(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(TestConfigurationHelper.GetDefaultTestSettings())
                    .Build()));

            // 注册领域服务
            services.AddScoped<MessageService>();
            services.AddScoped<AccountService>();
            services.AddScoped<AdminService>();
            services.AddScoped<ChatImportService>();
            services.AddScoped<CallbackDataService>();
            services.AddScoped<RefreshService>();

            // 注册AI服务
            services.AddScoped<AutoASRService>();
            services.AddScoped<AutoOCRService>();
            services.AddScoped<OllamaService>();
            services.AddScoped<OpenAIService>();
            services.AddScoped<GeminiService>();

            // 注册搜索服务
            services.AddScoped<SearchService>();
            services.AddScoped<LuceneManager>();

            // 注册向量服务
            services.AddScoped<IVectorGenerationService, TestVectorGenerationService>();
            services.AddScoped<IVectorSearchService, TestVectorSearchService>();

            // 注册存储服务
            services.AddScoped<SendMessage>();
            services.AddScoped<MessageService>();

            // 注册管理服务
            services.AddScoped<WordCloudTask>();
            services.AddScoped<ScheduledTaskExecutionService>();

            // 注册外部服务
            services.AddScoped<IBilibiliService, TestBilibiliService>();
        }

        /// <summary>
        /// 创建测试用的消息服务
        /// </summary>
        /// <param name="configure">配置回调</param>
        /// <returns>消息服务</returns>
        protected MessageService CreateMessageService(Action<MessageService>? configure = null)
        {
            var service = new MessageService(
                _serviceProvider.GetRequiredService<ILogger<MessageService>>(),
                _serviceProvider.GetRequiredService<LuceneManager>(),
                _serviceProvider.GetRequiredService<SendMessage>(),
                _dbContext,
                _serviceProvider.GetRequiredService<IMediator>());

            configure?.Invoke(service);
            return service;
        }

        /// <summary>
        /// 创建测试用的搜索服务
        /// </summary>
        /// <param name="configure">配置回调</param>
        /// <returns>搜索服务</returns>
        protected SearchService CreateSearchService(Action<SearchService>? configure = null)
        {
            var service = new SearchService(
                _serviceProvider.GetRequiredService<ILogger<SearchService>>(),
                _dbContext,
                _serviceProvider.GetRequiredService<LuceneManager>(),
                _serviceProvider.GetRequiredService<IVectorSearchService>());

            configure?.Invoke(service);
            return service;
        }

        /// <summary>
        /// 创建测试用的LLM服务
        /// </summary>
        /// <param name="configure">配置回调</param>
        /// <returns>LLM服务</returns>
        protected T CreateLLMService<T>(Action<T>? configure = null) where T : class, IGeneralLLMService
        {
            var service = _serviceProvider.GetRequiredService<T>();
            configure?.Invoke(service);
            return service;
        }

        /// <summary>
        /// 模拟Bot消息接收
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <returns>异步任务</returns>
        protected async Task SimulateBotMessageReceivedAsync(MessageOption message)
        {
            // 模拟Bot客户端接收消息
            _botClientMock.Setup(x => x.GetMeAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new Telegram.Bot.Types.User
                {
                    Id = 123456789,
                    FirstName = "Test",
                    LastName = "Bot",
                    Username = "testbot",
                    IsBot = true
                });

            // 模拟消息处理
            var messageService = CreateMessageService();
            await messageService.ProcessMessageAsync(message);
        }

        /// <summary>
        /// 模拟搜索请求
        /// </summary>
        /// <param name="searchQuery">搜索查询</param>
        /// <param name="chatId">聊天ID</param>
        /// <returns>搜索结果</returns>
        protected async Task<List<Message>> SimulateSearchRequestAsync(string searchQuery, long chatId)
        {
            var searchService = CreateSearchService();
            var searchOption = new TelegramSearchBot.Model.SearchOption
            {
                Search = searchQuery,
                ChatId = chatId,
                IsGroup = true,
                SearchType = SearchType.InvertedIndex,
                Skip = 0,
                Take = 10
            };

            return await searchService.SearchAsync(searchOption);
        }

        /// <summary>
        /// 模拟LLM请求
        /// </summary>
        /// <param name="prompt">提示词</param>
        /// <param name="response">响应</param>
        /// <returns>异步任务</returns>
        protected async Task SimulateLLMRequestAsync(string prompt, string response)
        {
            _llmServiceMock.Setup(x => x.ChatCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<System.Threading.CancellationToken>()
                ))
                .ReturnsAsync(response);

            var llmService = CreateLLMService<OpenAIService>();
            var result = await llmService.ChatCompletionAsync(prompt, "system");
            
            // 验证响应
            Assert.Equal(response, result);
        }

        /// <summary>
        /// 重置数据库
        /// </summary>
        /// <returns>异步任务</returns>
        protected async Task ResetDatabaseAsync()
        {
            await TestDatabaseHelper.ResetDatabaseAsync(_dbContext);
            _testData = await TestDatabaseHelper.CreateStandardTestDataAsync(_dbContext);
        }

        /// <summary>
        /// 创建数据库快照
        /// </summary>
        /// <returns>数据库快照</returns>
        protected async Task<DatabaseSnapshot> CreateDatabaseSnapshotAsync()
        {
            return await TestDatabaseHelper.CreateSnapshotAsync(_dbContext);
        }

        /// <summary>
        /// 从快照恢复数据库
        /// </summary>
        /// <param name="snapshot">数据库快照</param>
        /// <returns>异步任务</returns>
        protected async Task RestoreDatabaseFromSnapshotAsync(DatabaseSnapshot snapshot)
        {
            await TestDatabaseHelper.RestoreFromSnapshotAsync(_dbContext, snapshot);
        }

        /// <summary>
        /// 验证数据库状态
        /// </summary>
        /// <param name="expectedMessageCount">期望的消息数量</param>
        /// <param name="expectedUserCount">期望的用户数量</param>
        /// <param name="expectedGroupCount">期望的群组数量</param>
        /// <returns>异步任务</returns>
        protected async Task ValidateDatabaseStateAsync(int expectedMessageCount, int expectedUserCount, int expectedGroupCount)
        {
            var stats = await TestDatabaseHelper.GetDatabaseStatisticsAsync(_dbContext);
            
            Assert.Equal(expectedMessageCount, stats.MessageCount);
            Assert.Equal(expectedUserCount, stats.UserCount);
            Assert.Equal(expectedGroupCount, stats.GroupCount);
        }

        /// <summary>
        /// 验证Mock调用
        /// </summary>
        /// <param name="mock">Mock对象</param>
        /// <param name="expression">验证表达式</param>
        /// <param name="times">调用次数</param>
        /// <typeparam name="T">Mock类型</typeparam>
        protected void VerifyMockCall<T>(Mock<T> mock, System.Linq.Expressions.Expression<System.Action<T>> expression, Times? times = null) where T : class
        {
            mock.Verify(expression, times ?? Times.Once());
        }

        /// <summary>
        /// 验证Mock异步调用
        /// </summary>
        /// <param name="mock">Mock对象</param>
        /// <param name="expression">验证表达式</param>
        /// <param name="times">调用次数</param>
        /// <typeparam name="T">Mock类型</typeparam>
        /// <typeparam name="TResult">返回类型</typeparam>
        protected void VerifyMockAsyncCall<T, TResult>(Mock<T> mock, System.Linq.Expressions.Expression<System.Func<T, Task<TResult>>> expression, Times? times = null) where T : class
        {
            mock.Verify(expression, times ?? Times.Once());
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public virtual void Dispose()
        {
            _dbContext?.Dispose();
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            TestConfigurationHelper.CleanupTempConfigFile();
        }
    }

    /// <summary>
    /// 测试用的向量生成服务
    /// </summary>
    internal class TestVectorGenerationService : IVectorGenerationService
    {
        public Task<float[]> GenerateVectorAsync(string text, CancellationToken cancellationToken = default)
        {
            // 简化实现：生成基于文本长度的模拟向量
            var vector = new float[128];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(text.Length % 256) / 256f;
            }
            return Task.FromResult(vector);
        }

        public Task<float[][]> GenerateBatchVectorsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var vectors = texts.Select(text =>
            {
                var vector = new float[128];
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] = (float)(text.Length % 256) / 256f;
                }
                return vector;
            }).ToArray();
            return Task.FromResult(vectors);
        }

        public bool IsAvailable()
        {
            return true;
        }

        public string GetModelName()
        {
            return "test-vector-model";
        }

        public int GetVectorDimension()
        {
            return 128;
        }
    }

    /// <summary>
    /// 测试用的向量搜索服务
    /// </summary>
    internal class TestVectorSearchService : IVectorSearchService
    {
        public Task<List<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 10, CancellationToken cancellationToken = default)
        {
            // 简化实现：返回模拟搜索结果
            var results = new List<VectorSearchResult>
            {
                new VectorSearchResult
                {
                    Id = "1",
                    Score = 0.95f,
                    Content = "Test search result 1",
                    Metadata = new Dictionary<string, string> { { "type", "test" } }
                },
                new VectorSearchResult
                {
                    Id = "2",
                    Score = 0.85f,
                    Content = "Test search result 2",
                    Metadata = new Dictionary<string, string> { { "type", "test" } }
                }
            };
            return Task.FromResult(results);
        }

        public Task<bool> IndexDocumentAsync(string id, float[] vector, Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
        {
            // 简化实现：直接返回成功
            return Task.FromResult(true);
        }

        public Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default)
        {
            // 简化实现：直接返回成功
            return Task.FromResult(true);
        }

        public Task<bool> ClearIndexAsync(CancellationToken cancellationToken = default)
        {
            // 简化实现：直接返回成功
            return Task.FromResult(true);
        }

        public bool IsAvailable()
        {
            return true;
        }

        public int GetIndexSize()
        {
            return 1000; // 模拟索引大小
        }
    }

    /// <summary>
    /// 测试用的B站服务
    /// </summary>
    internal class TestBilibiliService : IBilibiliService
    {
        public Task<BilibiliVideoInfo> GetVideoInfoAsync(string bvid, CancellationToken cancellationToken = default)
        {
            // 简化实现：返回模拟视频信息
            var videoInfo = new BilibiliVideoInfo
            {
                Bvid = bvid,
                Title = "Test Video Title",
                Description = "Test video description",
                Author = "Test Author",
                PlayCount = 1000,
                LikeCount = 100,
                Duration = 300,
                PublishDate = DateTime.UtcNow.AddDays(-30)
            };
            return Task.FromResult(videoInfo);
        }

        public Task<string> ExtractVideoUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            // 简化实现：返回模拟视频URL
            return Task.FromResult("https://test.example.com/video.mp4");
        }

        public Task<bool> ValidateUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            // 简化实现：验证URL格式
            var isValid = url.Contains("bilibili.com") || url.Contains("b23.tv");
            return Task.FromResult(isValid);
        }

        public bool IsAvailable()
        {
            return true;
        }
    }

    /// <summary>
    /// 测试用的环境服务
    /// </summary>
    internal class TestEnvService : IEnvService
    {
        private readonly IConfiguration _configuration;

        public TestEnvService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Get(string key)
        {
            return _configuration[key] ?? string.Empty;
        }

        public T Get<T>(string key)
        {
            var value = _configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                return default(T);
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default(T);
            }
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(_configuration[key]);
        }

        public void Set(string key, string value)
        {
            // 测试环境不支持设置值
        }

        public void Remove(string key)
        {
            // 测试环境不支持删除值
        }

        public IEnumerable<string> GetKeys()
        {
            return _configuration.AsEnumerable().Select(x => x.Key);
        }

        public void Reload()
        {
            // 测试环境不支持重新加载
        }
    }

    /// <summary>
    /// 向量搜索结果
    /// </summary>
    public class VectorSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public float Score { get; set; }
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// B站视频信息
    /// </summary>
    public class BilibiliVideoInfo
    {
        public string Bvid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public int LikeCount { get; set; }
        public int Duration { get; set; }
        public DateTime PublishDate { get; set; }
    }
}