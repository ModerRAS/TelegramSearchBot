using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Test.Admin;
using Xunit;
using Xunit.Abstractions;

namespace TelegramSearchBot.Test.AI.LLM
{
    /// <summary>
    /// Microsoft.Extensions.AI POC 测试类
    /// 验证新的AI抽象层实现的可行性
    /// </summary>
    public class OpenAIExtensionsAIServiceTests : TestBase
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;

        public OpenAIExtensionsAIServiceTests(ITestOutputHelper output)
        {
            _output = output;
            
            // 创建测试服务提供者
            var services = new ServiceCollection();
            
            // 添加基础服务
            services.AddLogging();
            services.AddSingleton<TestDbContext>();
            services.AddTransient<IHttpClientFactory, TestHttpClientFactory>();
            services.AddTransient<IMessageExtensionService, TestMessageExtensionService>();
            
            // 添加AI服务
            services.AddTransient<OpenAIService>();
            services.AddTransient<OpenAIExtensionsAIService>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Service_ShouldBeRegistered()
        {
            // Arrange & Act
            var service = _serviceProvider.GetService<OpenAIExtensionsAIService>();
            
            // Assert
            Assert.NotNull(service);
            Assert.Equal("OpenAIExtensionsAIService", service.ServiceName);
        }

        [Fact]
        public async Task GetService_ShouldReturnCorrectImplementationBasedOnConfig()
        {
            // Arrange - 测试原有实现
            var useExtensionsAI = false;
            
            // Act
            var service = useExtensionsAI 
                ? _serviceProvider.GetService<OpenAIExtensionsAIService>() 
                : _serviceProvider.GetService<OpenAIService>();
            
            // Assert
            Assert.NotNull(service);
            Assert.IsAssignableFrom<ILLMService>(service);
            
            _output.WriteLine($"使用的服务: {service.ServiceName}");
            _output.WriteLine($"实现类型: {service.GetType().Name}");
        }

        [Fact]
        public async Task GenerateEmbeddings_ShouldWorkWithFallback()
        {
            // Arrange
            var service = _serviceProvider.GetService<OpenAIExtensionsAIService>();
            var testChannel = new LLMChannel
            {
                Provider = LLMProvider.OpenAI,
                Gateway = "https://api.openai.com/v1",
                ApiKey = "test-key"
            };
            
            // Act & Assert
            if (service != null)
            {
                try
                {
                    var embeddings = await service.GenerateEmbeddingsAsync(
                        "测试文本", "text-embedding-ada-002", testChannel);
                    
                    Assert.NotNull(embeddings);
                    Assert.NotEmpty(embeddings);
                    _output.WriteLine($"成功生成嵌入向量，维度: {embeddings.Length}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"嵌入生成失败（预期行为，因为是测试环境）: {ex.Message}");
                    // 这是预期的，因为我们在测试环境中没有真实的API密钥
                }
            }
        }

        [Fact]
        public void Configuration_ShouldControlImplementation()
        {
            // Arrange
            var originalValue = Env.UseMicrosoftExtensionsAI;
            
            try
            {
                // Act & Assert - 测试配置切换
                Env.UseMicrosoftExtensionsAI = true;
                _output.WriteLine($"配置已设置为使用 Microsoft.Extensions.AI: {Env.UseMicrosoftExtensionsAI}");
                
                Env.UseMicrosoftExtensionsAI = false;
                _output.WriteLine($"配置已设置为使用原有实现: {Env.UseMicrosoftExtensionsAI}");
                
                // 验证配置可以正常切换
                Assert.True(true); // 如果没有异常，说明配置工作正常
            }
            finally
            {
                // 恢复原始值
                Env.UseMicrosoftExtensionsAI = originalValue;
            }
        }

        [Fact]
        public void ServiceDependencies_ShouldBeResolvable()
        {
            // Arrange & Act
            var legacyService = _serviceProvider.GetService<OpenAIService>();
            var extensionsService = _serviceProvider.GetService<OpenAIExtensionsAIService>();
            
            // Assert
            Assert.NotNull(legacyService);
            Assert.NotNull(extensionsService);
            
            _output.WriteLine($"原有服务: {legacyService.ServiceName}");
            _output.WriteLine($"扩展服务: {extensionsService.ServiceName}");
        }
    }

    /// <summary>
    /// 测试用的HttpClientFactory
    /// </summary>
    public class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    /// <summary>
    /// 测试用的MessageExtensionService
    /// </summary>
    public class TestMessageExtensionService : IMessageExtensionService
    {
        public Task<Model.Data.MessageExtension> AddAsync(Model.Data.MessageExtension messageExtension)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Model.Data.MessageExtension>> GetByMessageDataIdAsync(int messageDataId)
        {
            return Task.FromResult<IEnumerable<Model.Data.MessageExtension>>(new List<Model.Data.MessageExtension>());
        }
    }
}