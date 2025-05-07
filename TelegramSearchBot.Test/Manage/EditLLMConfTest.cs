using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Test.Manage {
    [TestClass]
    public class EditLLMConfTest {
        private DataDbContext _context;
        private Mock<IConnectionMultiplexer> _redisMock;
        private Mock<IDatabase> _dbMock;
        private EditLLMConfService _service;

        [TestInitialize]
        public void Initialize() {
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
            
            _service = new EditLLMConfService(_context, _redisMock.Object);
        }

        [TestMethod]
        public async Task ExecuteAsync_NewChannel_CompleteFlow() {
            // Arrange
            long chatId = 123;
            // Setup state transitions
            _dbMock.SetupSequence(d => d.StringGetAsync("llmconf:123:state", It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("awaiting_name")  // After first command
                .ReturnsAsync("awaiting_gateway")
                .ReturnsAsync("awaiting_provider")
                .ReturnsAsync("awaiting_apikey");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync("llmconf:123:data", It.IsAny<CommandFlags>()))
                .ReturnsAsync("Test Channel")  // After name input
                .ReturnsAsync("Test Channel|http://test.com")  // After gateway input
                .ReturnsAsync("Test Channel|http://test.com|1");  // After provider selection

            // Act & Assert
            var result1 = await _service.ExecuteAsync("新建渠道", chatId);
            Assert.IsTrue(result1.Item1);
            Assert.AreEqual("请输入渠道的名称", result1.Item2);

            var result2 = await _service.ExecuteAsync("Test Channel", chatId);
            Assert.IsTrue(result2.Item1);
            Assert.AreEqual("请输入渠道地址", result2.Item2);

            var result3 = await _service.ExecuteAsync("http://test.com", chatId);
            Assert.IsTrue(result3.Item1);
            Assert.AreEqual("请选择渠道类型：\n1. OpenAI\n2. Ollama", result3.Item2);

            var result4 = await _service.ExecuteAsync("1", chatId);
            Assert.IsTrue(result4.Item1);
            Assert.AreEqual("请输入渠道的API Key", result4.Item2);

            var result5 = await _service.ExecuteAsync("test-api-key", chatId);
            Assert.IsTrue(result5.Item1);
            Assert.AreEqual("渠道创建成功", result5.Item2);

            // Verify channel was created
            var channel = await _context.LLMChannels.FirstOrDefaultAsync();
            Assert.IsNotNull(channel);
            Assert.AreEqual("Test Channel", channel.Name);
        }

        [TestMethod]
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
            Assert.IsTrue(result1.Item1);
            Assert.IsTrue(result1.Item2.Contains("请选择要添加模型的渠道ID："));

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.IsTrue(result2.Item1);
            Assert.AreEqual("请输入要添加的模型名称，多个模型用逗号或分号分隔", result2.Item2);

            var result3 = await _service.ExecuteAsync("model1,model2", chatId);
            Assert.IsTrue(result3.Item1);
            Assert.AreEqual("模型添加成功", result3.Item2);

            // Verify models were added
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.AreEqual(2, models.Count);
            Assert.IsTrue(models.Any(m => m.ModelName == "model1"));
            Assert.IsTrue(models.Any(m => m.ModelName == "model2"));
        }

        [TestMethod]
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
            _dbMock.SetupSequence(d => d.StringGetAsync("llmconf:123:state", It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)  // Initial state
                .ReturnsAsync("removing_model_select_channel")
                .ReturnsAsync("removing_model_select");

            // Setup data key responses
            _dbMock.SetupSequence(d => d.StringGetAsync("llmconf:123:data", It.IsAny<CommandFlags>()))
                .ReturnsAsync("1|model1,model2");  // After model selection

            // Act & Assert
            var result1 = await _service.ExecuteAsync("移除模型", chatId);
            Assert.IsTrue(result1.Item1);
            Assert.IsTrue(result1.Item2.Contains("请选择要移除模型的渠道ID："));

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.IsTrue(result2.Item1);
            Assert.IsTrue(result2.Item2.Contains("请选择要移除的模型："));

            var result3 = await _service.ExecuteAsync("1", chatId); // Remove model1
            Assert.IsTrue(result3.Item1);
            Assert.AreEqual("模型移除成功", result3.Item2);

            // Verify model was removed
            var models = await _context.ChannelsWithModel
                .Where(m => m.LLMChannelId == channel.Id)
                .ToListAsync();
            Assert.AreEqual(1, models.Count);
            Assert.AreEqual("model2", models[0].ModelName);
        }

        [TestMethod]
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
            Assert.IsTrue(result1.Item1);
            Assert.IsTrue(result1.Item2.Contains("请选择要查看模型的渠道ID："));

            var result2 = await _service.ExecuteAsync(channel.Id.ToString(), chatId);
            Assert.IsTrue(result2.Item1);
            Assert.IsTrue(result2.Item2.Contains("渠道 Test Channel 下的模型列表："));
            Assert.IsTrue(result2.Item2.Contains("- model1"));
            Assert.IsTrue(result2.Item2.Contains("- model2"));
        }

        [TestMethod]
        public async Task ExecuteAsync_InvalidState_ReturnsFailure() {
            // Arrange
            long chatId = 123;
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("invalid_state");

            // Act
            var result = await _service.ExecuteAsync("test", chatId);

            // Assert
            Assert.IsFalse(result.Item1);
            Assert.AreEqual("", result.Item2);
        }
    }
}
