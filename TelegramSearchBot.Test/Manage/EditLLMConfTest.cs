﻿#pragma warning disable CS8602 // 解引用可能出现空引用
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Interface.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Manage {
    public class EditLLMConfTest {
        private DataDbContext _context = null!;
        private Mock<IConnectionMultiplexer> _redisMock = null!;
        private Mock<IDatabase> _dbMock = null!;
        private Mock<OpenAIService> _openAIServiceMock = null!;
        private EditLLMConfService _service = null!;

        public EditLLMConfTest() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new DataDbContext(options);
            
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);
            
            // Setup Redis mock operations
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            
            // Setup Redis operations
            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            
            _dbMock.Setup(d => d.KeyDeleteAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            
            var loggerMock = new Mock<ILogger<OpenAIService>>();
            var messageExtensionServiceMock = new Mock<MessageExtensionService>(_context);
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _openAIServiceMock = new Mock<OpenAIService>(_context, loggerMock.Object, messageExtensionServiceMock.Object, httpClientFactoryMock.Object);
            _openAIServiceMock.Setup(o => o.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "model1", "model2" });

            var ollamaLoggerMock = new Mock<ILogger<OllamaService>>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var ollamaServiceMock = new Mock<OllamaService>(
                _context,
                ollamaLoggerMock.Object,
                serviceProviderMock.Object,
                httpClientFactoryMock.Object);
            ollamaServiceMock.Setup(o => o.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "ollama-model1", "ollama-model2" });

            var geminiLoggerMock = new Mock<ILogger<GeminiService>>();
            var geminiServiceMock = new Mock<GeminiService>(_context, geminiLoggerMock.Object, httpClientFactoryMock.Object);
            geminiServiceMock.Setup(g => g.GetAllModels(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<string> { "gemini-model1", "gemini-model2" });
            
            var llmFactoryMock = new Mock<ILLMFactory>();
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.OpenAI)).Returns(_openAIServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Ollama)).Returns(ollamaServiceMock.Object);
            llmFactoryMock.Setup(f => f.GetLLMService(LLMProvider.Gemini)).Returns(geminiServiceMock.Object);
            
            var helperMock = new Mock<EditLLMConfHelper>(
                _context,
                llmFactoryMock.Object);
            helperMock.Setup(h => h.AddChannel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LLMProvider>()))
                .ReturnsAsync(1);
            helperMock.Setup(h => h.AddModelWithChannel(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            helperMock.Setup(h => h.RemoveModelFromChannel(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            helperMock.Setup(h => h.UpdateChannel(
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<LLMProvider>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
                .ReturnsAsync(true);
            
            _service = new EditLLMConfService(
                helperMock.Object,
                _context,
                _redisMock.Object);
        }

        [Fact]
        public async Task ExecuteAsync_NewChannel_CompleteFlow() {
            // Arrange
            long chatId = 123;
            // Setup state transitions
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("awaiting_name")  // After first command
                .ReturnsAsync("awaiting_gateway")
                .ReturnsAsync("awaiting_provider")
                .ReturnsAsync("awaiting_parallel")
                .ReturnsAsync("awaiting_priority")
                .ReturnsAsync("awaiting_apikey");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("Test Channel")  // After name input
                .ReturnsAsync("Test Channel|http://test.com")  // After gateway input
                .ReturnsAsync("Test Channel|http://test.com|1")  // After provider selection
                .ReturnsAsync("Test Channel|http://test.com|1|1")  // After parallel input (default 1)
                .ReturnsAsync("Test Channel|http://test.com|1|1|0");  // After priority input (default 0)

            // Act & Assert
            var result1 = await _service.ExecuteAsync("新建渠道", chatId);
            Assert.True(result1.Item1);
            Assert.Equal("请输入渠道的名称", result1.Item2);

            var result2 = await _service.ExecuteAsync("Test Channel", chatId);
            Assert.True(result2.Item1);
            Assert.Equal("请输入渠道地址", result2.Item2);

            var result3 = await _service.ExecuteAsync("http://test.com", chatId);
            Assert.True(result3.Item1);
            Assert.Contains("请选择渠道类型：", result3.Item2);
            Assert.Contains("1. OpenAI", result3.Item2);
            Assert.Contains("2. Ollama", result3.Item2); 
            Assert.Contains("3. Gemini", result3.Item2);

            var result4 = await _service.ExecuteAsync("1", chatId);
            Assert.True(result4.Item1);
            Assert.Equal("请输入渠道的最大并行数量(默认1):", result4.Item2);

            var result5 = await _service.ExecuteAsync("", chatId); // 使用默认值1
            Assert.True(result5.Item1);
            Assert.Equal("请输入渠道的优先级(默认0):", result5.Item2);

            var result6 = await _service.ExecuteAsync("", chatId); // 使用默认值0
            Assert.True(result6.Item1);
            Assert.Equal("请输入渠道的API Key", result6.Item2);

            var result7 = await _service.ExecuteAsync("test-api-key", chatId);
            Assert.True(result7.Item1);
            Assert.Equal("渠道创建成功", result7.Item2);

            // Verify channel was created
            var channel = await _context.LLMChannels.FirstOrDefaultAsync();
            Assert.NotNull(channel);
            Assert.Equal("Test Channel", channel.Name);
            Assert.Equal(1, channel.Parallel); // 验证默认并行数
            Assert.Equal(0, channel.Priority); // 验证默认优先级
        }

        [Fact]
        public async Task ExecuteAsync_AddModel_CompleteFlow() {
            // Arrange
            long chatId = 123;
            var channel = new LLMChannel {
                Name = "Test Channel",
                Gateway = "http://test.com",
                ApiKey = "test-key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("adding_model_select_channel")
                .ReturnsAsync("adding_model_input");

            // Setup data key to return channel ID
            _dbMock.Setup(d => d.StringGetAsync("llmconf:123:data", It.IsAny<CommandFlags>()))
                .ReturnsAsync(channel.Id.ToString());

            // Act & Assert
            var result1 = await _service.ExecuteAsync("添加模型", chatId);
            Assert.True(result1.Item1);
            Assert.Contains("请选择要添加模型的渠道ID：", result1.Item2);

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.True(result2.Item1);
            Assert.Equal("请输入要添加的模型名称，多个模型用逗号或分号分隔", result2.Item2);

            var result3 = await _service.ExecuteAsync("model1,model2", chatId);
            Assert.True(result3.Item1);
            Assert.Equal("模型添加成功", result3.Item2);

            // Verify models were added
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.Equal(2, models.Count);
            Assert.Contains(models, m => m.ModelName == "model1");
            Assert.Contains(models, m => m.ModelName == "model2");
        }

        [Fact]
        public async Task ExecuteAsync_RemoveModel_CompleteFlow() {
            // Arrange
            long chatId = 123;
            var channel = new LLMChannel {
                Name = "Test Channel",
                Gateway = "http://test.com",
                ApiKey = "test-key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddRangeAsync(
                new ChannelWithModel { LLMChannelId = channel.Id, ModelName = "model1" },
                new ChannelWithModel { LLMChannelId = channel.Id, ModelName = "model2" }
            );
            await _context.SaveChangesAsync();

            // Setup state transitions
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("removing_model_select_channel")
                .ReturnsAsync("removing_model_select");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("1|model1,model2");  // After model selection

            // Act & Assert
            var result1 = await _service.ExecuteAsync("移除模型", chatId);
            Assert.True(result1.Item1);
            Assert.Contains("请选择要移除模型的渠道ID：", result1.Item2);

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.True(result2.Item1);
            Assert.Contains("请选择要移除的模型：", result2.Item2);

            var result3 = await _service.ExecuteAsync("1", chatId); // Remove model1
            Assert.True(result3.Item1);
            Assert.Equal("模型移除成功", result3.Item2);

            // Verify model was removed
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.Single(models);
            Assert.Equal("model2", models[0].ModelName);
        }

        [Fact]
        public async Task ExecuteAsync_ViewModels_CompleteFlow() {
            // Arrange
            long chatId = 123;
            var channel = new LLMChannel {
                Name = "Test Channel",
                Gateway = "http://test.com",
                ApiKey = "test-key",
                Provider = LLMProvider.OpenAI
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.ChannelsWithModel.AddRangeAsync(
                new ChannelWithModel { LLMChannelId = channel.Id, ModelName = "model1" },
                new ChannelWithModel { LLMChannelId = channel.Id, ModelName = "model2" }
            );
            await _context.SaveChangesAsync();

            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("viewing_model_select_channel");

            // Act & Assert
            var result1 = await _service.ExecuteAsync("查看模型", chatId);
            Assert.True(result1.Item1);
            Assert.Contains("请选择要查看模型的渠道ID：", result1.Item2);

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.True(result2.Item1);
            Assert.Contains("渠道 Test Channel 下的模型列表：", result2.Item2);
            Assert.Contains("- model1", result2.Item2);
            Assert.Contains("- model2", result2.Item2);
        }

        [Fact]
        public async Task ExecuteAsync_InvalidState_ReturnsFailure() {
            // Arrange
            long chatId = 123;
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("invalid_state");

            // Act
            var result = await _service.ExecuteAsync("test", chatId);

            // Assert
            Assert.False(result.Item1);
            Assert.Equal("", result.Item2);
        }

        [Fact]
        public async Task ExecuteAsync_NewChannel_WithParallelAndPriority() {
            // Arrange
            long chatId = 123;
            // Setup state transitions
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("awaiting_name")  
                .ReturnsAsync("awaiting_gateway")
                .ReturnsAsync("awaiting_provider")
                .ReturnsAsync("awaiting_parallel")
                .ReturnsAsync("awaiting_priority")
                .ReturnsAsync("awaiting_apikey");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("Test Channel")  
                .ReturnsAsync("Test Channel|http://test.com")  
                .ReturnsAsync("Test Channel|http://test.com|1")
                .ReturnsAsync("Test Channel|http://test.com|1|5")  // Parallel = 5
                .ReturnsAsync("Test Channel|http://test.com|1|5|2");  // Priority = 2

            // Act & Assert
            var result1 = await _service.ExecuteAsync("新建渠道", chatId);
            Assert.True(result1.Item1);
            Assert.Equal("请输入渠道的名称", result1.Item2);

            var result2 = await _service.ExecuteAsync("Test Channel", chatId);
            Assert.True(result2.Item1);
            Assert.Equal("请输入渠道地址", result2.Item2);

            var result3 = await _service.ExecuteAsync("http://test.com", chatId);
            Assert.True(result3.Item1);
            Assert.Contains("请选择渠道类型：", result3.Item2);
            Assert.Contains("1. OpenAI", result3.Item2);
            Assert.Contains("2. Ollama", result3.Item2);
            Assert.Contains("3. Gemini", result3.Item2);

            var result4 = await _service.ExecuteAsync("1", chatId);
            Assert.True(result4.Item1);
            Assert.Equal("请输入渠道的最大并行数量(默认1):", result4.Item2);

            var result5 = await _service.ExecuteAsync("5", chatId);
            Assert.True(result5.Item1);
            Assert.Equal("请输入渠道的优先级(默认0):", result5.Item2);

            var result6 = await _service.ExecuteAsync("2", chatId);
            Assert.True(result6.Item1);
            Assert.Equal("请输入渠道的API Key", result6.Item2);

            var result7 = await _service.ExecuteAsync("test-api-key", chatId);
            Assert.True(result7.Item1);
            Assert.Equal("渠道创建成功", result7.Item2);

            // Verify channel was created with correct values
            var channel = await _context.LLMChannels.FirstOrDefaultAsync();
            Assert.NotNull(channel);
            Assert.Equal("Test Channel", channel.Name);
            Assert.Equal(5, channel.Parallel);
            Assert.Equal(2, channel.Priority);
        }

        [Fact]
        public async Task ExecuteAsync_SetMaxRetryCount() {
            // Arrange
            long chatId = 123;
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("setting_max_retry");

            // Act & Assert
            var result1 = await _service.ExecuteAsync("设置重试次数", chatId);
            Assert.True(result1.Item1);
            Assert.Equal("请输入最大重试次数(默认100):", result1.Item2);

            var result2 = await _service.ExecuteAsync("50", chatId);
            Assert.True(result2.Item1);
            Assert.Equal("最大重试次数已设置为: 50", result2.Item2);

            // Verify database update
            var config = await _context.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.MaxRetryCountKey);
            Assert.NotNull(config);
            Assert.Equal("50", config.Value);
        }

        [Fact]
        public async Task ExecuteAsync_SetMaxImageRetryCount() {
            // Arrange
            long chatId = 123;
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("setting_max_image_retry");

            // Act & Assert
            var result1 = await _service.ExecuteAsync("设置图片重试次数", chatId);
            Assert.True(result1.Item1);
            Assert.Equal("请输入图片处理最大重试次数(默认1000):", result1.Item2);

            var result2 = await _service.ExecuteAsync("500", chatId);
            Assert.True(result2.Item1);
            Assert.Equal("图片处理最大重试次数已设置为: 500", result2.Item2);

            // Verify database update
            var config = await _context.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.MaxImageRetryCountKey);
            Assert.NotNull(config);
            Assert.Equal("500", config.Value);
        }

        [Fact]
        public async Task ExecuteAsync_SetMaxRetryCount_InvalidInput() {
            // Arrange
            long chatId = 123;
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("setting_max_retry");

            // Act & Assert
            var result1 = await _service.ExecuteAsync("设置重试次数", chatId);
            Assert.True(result1.Item1);
            Assert.Equal("请输入最大重试次数(默认100):", result1.Item2);

            var result2 = await _service.ExecuteAsync("invalid", chatId);
            Assert.False(result2.Item1);
            Assert.Equal("请输入有效的正整数", result2.Item2);

            // Verify no database update
            var config = await _context.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.MaxRetryCountKey);
            Assert.Null(config);
        }

        [Fact]
        public async Task ExecuteAsync_UpdateParallelAndPriority() {
            // Arrange
            long chatId = 123;
            var channel = new LLMChannel {
                Name = "Test Channel",
                Gateway = "http://test.com",
                ApiKey = "test-key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 0
            };
            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();

            // Setup state transitions
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("editing_select_channel")
                .ReturnsAsync("editing_select_field")
                .ReturnsAsync("editing_input_value")
                .ReturnsAsync("editing_select_field")
                .ReturnsAsync("editing_input_value");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(channel.Id.ToString())
                .ReturnsAsync($"{channel.Id}|5")  // Field 5 = Parallel
                .ReturnsAsync($"{channel.Id}")  // Field 6 = Priority
                .ReturnsAsync($"{channel.Id}|6")  // For parallel update
                .ReturnsAsync($"{channel.Id}"); // For priority update

            // Act & Assert
            var result1 = await _service.ExecuteAsync("编辑渠道", chatId);
            Assert.True(result1.Item1);
            Assert.Contains("请选择要编辑的渠道ID：", result1.Item2);

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.True(result2.Item1);
            Assert.Contains("请选择要编辑的字段：", result2.Item2);

            var result3 = await _service.ExecuteAsync("5", chatId); // Select Parallel
            Assert.True(result3.Item1);
            Assert.Equal("请输入新的值：", result3.Item2);

            var result4 = await _service.ExecuteAsync("10", chatId); // Update Parallel to 10
            Assert.True(result4.Item1);
            Assert.Equal("更新成功", result4.Item2);
            
            var result5 = await _service.ExecuteAsync("6", chatId); // Select Priority
            Assert.True(result5.Item1);
            Assert.Equal("请输入新的值：", result5.Item2);

            var result6 = await _service.ExecuteAsync("3", chatId); // Update Priority to 3
            Assert.True(result6.Item1);
            Assert.Equal("更新成功", result6.Item2);

            // Verify updates
            var updatedChannel = await _context.LLMChannels.FindAsync(channel.Id);
            Assert.Equal(10, updatedChannel.Parallel);
            Assert.Equal(3, updatedChannel.Priority);
        }

    }
}
