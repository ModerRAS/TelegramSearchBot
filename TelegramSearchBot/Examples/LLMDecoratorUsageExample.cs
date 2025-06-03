using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM.Decorators;
using TelegramSearchBot.Service.AI.LLM.Adapters;

namespace TelegramSearchBot.Examples
{
    /// <summary>
    /// LLM装饰器使用示例
    /// </summary>
    public class LLMDecoratorUsageExample
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LLMDecoratorUsageExample> _logger;

        public LLMDecoratorUsageExample(
            IServiceProvider serviceProvider,
            ILogger<LLMDecoratorUsageExample> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 示例1：基本装饰器使用
        /// </summary>
        public async Task BasicDecoratorExample()
        {
            // 获取装饰器工厂
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            
            // 获取原始LLM服务（例如OllamaService）
            var originalService = _serviceProvider.GetRequiredService<OllamaService>();
            
            // 创建适配器
            var adapter = new LLMServiceAdapter(originalService);
            
            // 应用基本装饰器（日志 + 流控）
            var decoratedService = decoratorFactory.CreateBasicDecorator(adapter);
            
            // 使用装饰过的服务
            var message = new Message { Content = "Hello, AI!" };
            var chatId = 12345L;
            var modelName = "llama3:8b";
            var channel = new LLMChannel 
            { 
                Id = 1, 
                Provider = LLMProvider.Ollama, 
                Gateway = "http://localhost:11434",
                Parallel = 2
            };

            await foreach (var token in decoratedService.ExecAsync(message, chatId, modelName, channel, CancellationToken.None))
            {
                Console.Write(token);
            }
        }

        /// <summary>
        /// 示例2：完整装饰器使用（包含工具调用）
        /// </summary>
        public async Task FullDecoratorExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            var originalService = _serviceProvider.GetRequiredService<OpenAIService>();
            var adapter = new LLMServiceAdapter(originalService);
            
            // 应用完整装饰器（日志 + 流控 + 工具调用）
            var decoratedService = decoratorFactory.CreateFullDecorator(adapter, "AI Assistant");
            
            var message = new Message { Content = "搜索今天的天气" };
            var chatId = 12345L;
            var modelName = "gpt-4";
            var channel = new LLMChannel 
            { 
                Id = 2, 
                Provider = LLMProvider.OpenAI, 
                Gateway = "https://api.openai.com",
                Parallel = 5
            };

            await foreach (var token in decoratedService.ExecAsync(message, chatId, modelName, channel, CancellationToken.None))
            {
                Console.Write(token);
            }
        }

        /// <summary>
        /// 示例3：自定义装饰器组合
        /// </summary>
        public async Task CustomDecoratorExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            var originalService = _serviceProvider.GetRequiredService<GeminiService>();
            var adapter = new LLMServiceAdapter(originalService);
            
            // 自定义装饰器选项
            var options = new LLMDecoratorOptions
            {
                EnableLogging = true,
                EnableRateLimit = true,
                EnableToolInvocation = true,
                BotName = "专业AI助手",
                MaxToolCycles = 3,
                MaxRetries = 50,
                RetryDelay = TimeSpan.FromSeconds(2)
            };
            
            var decoratedService = decoratorFactory.CreateDecoratedService(adapter, options);
            
            var message = new Message { Content = "分析这张图片并搜索相关信息" };
            var chatId = 12345L;
            var modelName = "gemini-pro-vision";
            var channel = new LLMChannel 
            { 
                Id = 3, 
                Provider = LLMProvider.Gemini, 
                Gateway = "https://generativelanguage.googleapis.com",
                Parallel = 3
            };

            await foreach (var token in decoratedService.ExecAsync(message, chatId, modelName, channel, CancellationToken.None))
            {
                Console.Write(token);
            }
        }

        /// <summary>
        /// 示例4：使用渠道选择装饰器
        /// </summary>
        public async Task ChannelSelectionExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            
            // 创建渠道选择装饰器
            var channelSelector = decoratorFactory.CreateChannelSelectionDecorator();
            
            var message = new Message { Content = "你好，请介绍一下自己" };
            var chatId = 12345L;
            var modelName = "gpt-4"; // 这个模型可能配置了多个渠道
            
            // 不指定特定渠道，让装饰器自动选择最佳渠道
            await foreach (var token in channelSelector.ExecAsync(message, chatId, modelName, null, CancellationToken.None))
            {
                Console.Write(token);
            }
        }

        /// <summary>
        /// 示例5：获取可用容量信息
        /// </summary>
        public async Task CapacityMonitoringExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            var channelSelector = decoratorFactory.CreateChannelSelectionDecorator();
            
            // 获取指定模型的可用容量
            var capacity = await channelSelector.GetAvailableCapacityAsync("gpt-4");
            _logger.LogInformation("GPT-4模型当前可用容量: {Capacity}", capacity);
            
            // 获取所有渠道的健康状态
            var decoratedService = _serviceProvider.GetRequiredService<DecoratedGeneralLLMService>();
            var channels = await decoratedService.GetChannelsAsync("gpt-4");
            
            foreach (var channel in channels)
            {
                var isHealthy = await channelSelector.IsHealthyAsync(channel);
                _logger.LogInformation("渠道 {ChannelId} ({Provider}) 健康状态: {IsHealthy}", 
                    channel.Id, channel.Provider, isHealthy);
            }
        }

        /// <summary>
        /// 示例6：处理工具调用
        /// </summary>
        public async Task ToolInvocationExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            var originalService = _serviceProvider.GetRequiredService<OpenAIService>();
            var adapter = new LLMServiceAdapter(originalService);
            
            // 只启用工具调用装饰器
            var options = new LLMDecoratorOptions
            {
                EnableLogging = false,
                EnableRateLimit = false,
                EnableToolInvocation = true,
                BotName = "工具助手",
                MaxToolCycles = 5
            };
            
            var decoratedService = decoratorFactory.CreateDecoratedService(adapter, options);
            
            // 这个消息可能会触发工具调用
            var message = new Message { Content = "搜索'人工智能发展趋势'相关信息，并计算结果数量" };
            var chatId = 12345L;
            var modelName = "gpt-4";
            var channel = new LLMChannel 
            { 
                Id = 2, 
                Provider = LLMProvider.OpenAI, 
                Gateway = "https://api.openai.com",
                Parallel = 5
            };

            _logger.LogInformation("开始工具调用示例...");
            
            await foreach (var token in decoratedService.ExecAsync(message, chatId, modelName, channel, CancellationToken.None))
            {
                Console.Write(token);
            }
            
            _logger.LogInformation("工具调用示例完成");
        }

        /// <summary>
        /// 示例7：错误处理和重试
        /// </summary>
        public async Task ErrorHandlingExample()
        {
            var decoratorFactory = _serviceProvider.GetRequiredService<LLMDecoratorFactory>();
            var channelSelector = decoratorFactory.CreateChannelSelectionDecorator();
            
            try
            {
                var message = new Message { Content = "测试错误处理" };
                var chatId = 12345L;
                var modelName = "non-existent-model"; // 不存在的模型
                
                await foreach (var token in channelSelector.ExecAsync(message, chatId, modelName, null, CancellationToken.None))
                {
                    Console.Write(token);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "预期的错误：{Error}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "意外错误：{Error}", ex.Message);
            }
        }
    }
}

// 服务注册示例
namespace TelegramSearchBot.Examples
{
    /// <summary>
    /// DI容器配置示例
    /// </summary>
    public static class ServiceRegistrationExample
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            // 注册核心服务
            services.AddScoped<LLMDecoratorFactory>();
            services.AddScoped<DecoratedGeneralLLMService>();
            
            // 注册装饰器
            services.AddScoped<RateLimitDecorator>();
            services.AddScoped<LoggingDecorator>();
            services.AddScoped<ToolInvocationDecorator>();
            services.AddScoped<ChannelSelectionDecorator>();
            
            // 注册原始LLM服务
            services.AddTransient<OllamaService>();
            services.AddTransient<OpenAIService>();
            services.AddTransient<GeminiService>();
            
            // 可以根据需要注册为单例
            services.AddSingleton<IGeneralLLMService>(provider => 
                provider.GetRequiredService<DecoratedGeneralLLMService>());
        }
    }
} 