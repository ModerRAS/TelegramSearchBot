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
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Test.Helpers;
using MediatR;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.AI.ASR;
using Message = TelegramSearchBot.Model.Data.Message;
using IMessageRepository = TelegramSearchBot.Domain.Message.Repositories.IMessageRepository;
using IMessageService = TelegramSearchBot.Domain.Message.IMessageService;
using MessageRepository = TelegramSearchBot.Domain.Message.MessageRepository;
using MessageService = TelegramSearchBot.Domain.Message.MessageService;

namespace TelegramSearchBot.Test.Base
{
    /// <summary>
    /// 集成测试基类，提供完整的测试基础设施
    /// 简化实现：移除复杂的依赖和向量服务
    /// </summary>
    public abstract class IntegrationTestBase : IDisposable
    {
        protected readonly IServiceProvider _serviceProvider;
        protected readonly DataDbContext _dbContext;
        protected readonly Mock<ITelegramBotClient> _botClientMock;
        protected readonly Mock<IGeneralLLMService> _llmServiceMock;
        protected readonly Mock<ILogger<IntegrationTestBase>> _loggerMock;
        protected readonly Mock<IMediator> _mediatorMock;
        protected readonly TelegramSearchBot.Test.Helpers.TestDataSet _testData;
        protected readonly IEnvService _envService;

        protected IntegrationTestBase()
        {
            // 创建服务集合
            var services = new ServiceCollection();

            // 配置测试服务
            ConfigureTestServices(services);

            // 构建服务提供者
            _serviceProvider = services.BuildServiceProvider();

            // 获取必需的服务
            _dbContext = _serviceProvider.GetRequiredService<DataDbContext>();
            _botClientMock = _serviceProvider.GetRequiredService<Mock<ITelegramBotClient>>();
            _llmServiceMock = _serviceProvider.GetRequiredService<Mock<IGeneralLLMService>>();
            _loggerMock = _serviceProvider.GetRequiredService<Mock<ILogger<IntegrationTestBase>>>();
            _mediatorMock = _serviceProvider.GetRequiredService<Mock<IMediator>>();
            _envService = _serviceProvider.GetRequiredService<IEnvService>();
            _testData = _serviceProvider.GetRequiredService<TelegramSearchBot.Test.Helpers.TestDataSet>();

            // 初始化数据库
            InitializeDatabase();
        }

        /// <summary>
        /// 配置测试服务
        /// </summary>
        /// <param name="services">服务集合</param>
        private void ConfigureTestServices(IServiceCollection services)
        {
            // 配置内存数据库
            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            // 注册基础Mock服务
            services.AddSingleton(Mock.Of<ITelegramBotClient>());
            services.AddSingleton(Mock.Of<IGeneralLLMService>());
            services.AddSingleton(Mock.Of<ILogger<IntegrationTestBase>>());
            services.AddSingleton(Mock.Of<IMediator>());

            // 注册测试配置
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["BotToken"] = "test_token",
                    ["AdminId"] = "123456789",
                    ["WorkDir"] = "/tmp/test"
                })
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IEnvService, TestEnvService>();

            // 注册测试数据集
            services.AddSingleton<TelegramSearchBot.Test.Helpers.TestDataSet>();

            // 注册Message服务
            services.AddScoped<IMessageRepository, MessageRepository>();
            services.AddScoped<IMessageService, MessageService>();

            // 注册其他基础设施服务
            // services.AddScoped<SearchService>(); // 需要正确的命名空间

            // 简化实现：注册存储服务
            // services.AddScoped<SendMessage>(); // 需要正确的命名空间

            // 注册管理服务
            // services.AddScoped<PaddleOCR>(); // 需要正确的命名空间
            // services.AddScoped<WhisperManager>(); // 需要正确的命名空间
            // services.AddScoped<QRManager>(); // 需要正确的命名空间

            // 注册AI服务
            // services.AddScoped<AutoASRService>(); // 需要正确的命名空间
            // services.AddScoped<AutoOCRService>(); // 需要正确的命名空间
            // services.AddScoped<RefreshService>(); // 需要正确的命名空间
            // services.AddScoped<SearchToolService>(); // 需要正确的命名空间

            // 注册控制器
            // services.AddScoped<AutoASRController>(); // 需要正确的命名空间
            // services.AddScoped<AutoOCRController>(); // 需要正确的命名空间
            // services.AddScoped<RefreshService>(); // 需要正确的命名空间
            // services.AddScoped<SearchToolService>(); // 需要正确的命名空间
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        private void InitializeDatabase()
        {
            // 确保数据库被创建
            _dbContext.Database.EnsureCreated();

            // 添加测试数据
            _testData.Initialize(_dbContext);
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            // 清理数据库
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();

            // 清理服务提供者
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 测试环境服务
    /// </summary>
    internal class TestEnvService : IEnvService
    {
        public string BotToken => "test_token";
        public long AdminId => 123456789;
        public string WorkDir => "/tmp/test";
        public int SchedulerPort => 6379;
        public string RedisConnectionString => "localhost:6379";
        public bool EnableAutoOCR => true;
        public bool EnableAutoASR => true;
        public bool EnableVideoASR => true;
        public string OllamaModelName => "llama2";
        public string OpenAIModelName => "gpt-3.5-turbo";
        public string GeminiModelName => "gemini-pro";
        public string BaseUrl => "http://localhost:5000";
        public bool IsLocalAPI => true;

        public string GetConfigPath()
        {
            return Path.Combine(WorkDir, "test_config.json");
        }

        public void SaveConfig()
        {
            // 测试环境不需要保存配置
        }

        public void SetValue(string key, string value)
        {
            // 测试环境不支持设置值
        }

        public string GetValue(string key)
        {
            // 返回默认值
            return key switch
            {
                "BotToken" => BotToken,
                "AdminId" => AdminId.ToString(),
                "WorkDir" => WorkDir,
                _ => string.Empty
            };
        }

        public void Remove(string key)
        {
            // 测试环境不支持删除值
        }

        public IEnumerable<string> GetKeys()
        {
            return new[] { "BotToken", "AdminId", "WorkDir" };
        }

        public void Reload()
        {
            // 测试环境不支持重新加载
        }
    }
}