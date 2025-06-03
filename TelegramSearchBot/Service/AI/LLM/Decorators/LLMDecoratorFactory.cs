using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// LLM装饰器工厂 - 用于创建和组合各种装饰器
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class LLMDecoratorFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly DataDbContext _dbContext;
        private readonly ILLMFactory _llmFactory;
        private readonly ILoggerFactory _loggerFactory;

        public LLMDecoratorFactory(
            IServiceProvider serviceProvider,
            IConnectionMultiplexer connectionMultiplexer,
            DataDbContext dbContext,
            ILLMFactory llmFactory,
            ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _llmFactory = llmFactory ?? throw new ArgumentNullException(nameof(llmFactory));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <summary>
        /// 创建带有所有装饰器的LLM服务
        /// </summary>
        public ILLMStreamService CreateDecoratedService(
            ILLMStreamService baseService, 
            LLMDecoratorOptions options = null)
        {
            options ??= new LLMDecoratorOptions();
            ILLMStreamService service = baseService;

            // 1. 日志装饰器（最外层）
            if (options.EnableLogging)
            {
                var logger = _loggerFactory.CreateLogger<LoggingDecorator>();
                service = new LoggingDecorator(service, logger);
            }

            // 2. 工具调用装饰器
            if (options.EnableToolInvocation)
            {
                var logger = _loggerFactory.CreateLogger<ToolInvocationDecorator>();
                service = new ToolInvocationDecorator(
                    service, 
                    logger, 
                    options.MaxToolCycles, 
                    options.BotName);
            }

            // 3. 流控装饰器
            if (options.EnableRateLimit)
            {
                var logger = _loggerFactory.CreateLogger<RateLimitDecorator>();
                service = new RateLimitDecorator(
                    service, 
                    _connectionMultiplexer, 
                    logger, 
                    options.MaxRetries, 
                    options.RetryDelay);
            }

            return service;
        }

        /// <summary>
        /// 创建渠道选择装饰器
        /// </summary>
        public ChannelSelectionDecorator CreateChannelSelectionDecorator(
            LLMDecoratorOptions options = null)
        {
            options ??= new LLMDecoratorOptions();
            
            var logger = _loggerFactory.CreateLogger<ChannelSelectionDecorator>();
            
            // 创建装饰器工厂函数
            Func<ILLMStreamService, ILLMStreamService> decoratorFactory = (baseService) =>
                CreateDecoratedService(baseService, options);

            return new ChannelSelectionDecorator(
                _dbContext,
                _llmFactory,
                decoratorFactory,
                logger);
        }

        /// <summary>
        /// 创建简单的装饰器组合
        /// </summary>
        public ILLMStreamService CreateBasicDecorator(ILLMStreamService baseService)
        {
            return CreateDecoratedService(baseService, new LLMDecoratorOptions
            {
                EnableLogging = true,
                EnableRateLimit = true,
                EnableToolInvocation = false
            });
        }

        /// <summary>
        /// 创建完整的装饰器组合（包含工具调用）
        /// </summary>
        public ILLMStreamService CreateFullDecorator(ILLMStreamService baseService, string botName = "AI Assistant")
        {
            return CreateDecoratedService(baseService, new LLMDecoratorOptions
            {
                EnableLogging = true,
                EnableRateLimit = true,
                EnableToolInvocation = true,
                BotName = botName,
                MaxToolCycles = 5
            });
        }

        /// <summary>
        /// 从配置创建装饰器
        /// </summary>
        public ILLMStreamService CreateFromConfiguration(
            ILLMStreamService baseService, 
            string configurationKey = "LLM:Decorators")
        {
            // 这里可以从配置文件或数据库读取装饰器选项
            var options = GetOptionsFromConfiguration(configurationKey);
            return CreateDecoratedService(baseService, options);
        }

        /// <summary>
        /// 从配置获取选项（可以扩展为从配置文件读取）
        /// </summary>
        private LLMDecoratorOptions GetOptionsFromConfiguration(string configurationKey)
        {
            // 默认配置，可以扩展为从 IConfiguration 读取
            return new LLMDecoratorOptions
            {
                EnableLogging = true,
                EnableRateLimit = true,
                EnableToolInvocation = true,
                BotName = "AI Assistant",
                MaxToolCycles = 5,
                MaxRetries = 100,
                RetryDelay = TimeSpan.FromSeconds(5)
            };
        }
    }

    /// <summary>
    /// LLM装饰器选项
    /// </summary>
    public class LLMDecoratorOptions
    {
        /// <summary>
        /// 启用日志装饰器
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// 启用流控装饰器
        /// </summary>
        public bool EnableRateLimit { get; set; } = true;

        /// <summary>
        /// 启用工具调用装饰器
        /// </summary>
        public bool EnableToolInvocation { get; set; } = true;

        /// <summary>
        /// 机器人名称
        /// </summary>
        public string BotName { get; set; } = "AI Assistant";

        /// <summary>
        /// 最大工具调用循环次数
        /// </summary>
        public int MaxToolCycles { get; set; } = 5;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 100;

        /// <summary>
        /// 重试延迟时间
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
} 