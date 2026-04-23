using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Manage;
using Xunit;

namespace TelegramSearchBot.Test.Manage {
    public class EditOCRConfServiceTests {
        private readonly DataDbContext _dbContext;
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Dictionary<string, RedisValue> _redisStore = new();
        private readonly EditOCRConfService _service;

        public EditOCRConfServiceTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _dbContext = new DataDbContext(options);

            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);

            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags flags) =>
                    _redisStore.TryGetValue(key.ToString(), out var value) ? value : RedisValue.Null);

            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, When when) => {
                    _redisStore[key.ToString()] = value;
                    return true;
                });

            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) => {
                    _redisStore[key.ToString()] = value;
                    return true;
                });

            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when, CommandFlags flags) => {
                    _redisStore[key.ToString()] = value;
                    return true;
                });

            _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags flags) => _redisStore.Remove(key.ToString()));

            var loggerMock = new Mock<ILogger<EditOCRConfService>>();
            _service = new EditOCRConfService(_dbContext, _redisMock.Object, loggerMock.Object);
        }

        [Fact]
        public async Task ExecuteAsync_UnrelatedCommandWithoutActiveSession_ReturnsFalse() {
            var (status, message) = await _service.ExecuteAsync("你好", 12345);

            Assert.False(status);
            Assert.Equal(string.Empty, message);
        }

        [Fact]
        public async Task ExecuteAsync_SwitchEngineFlow_UsesReplyKeyboardAndTransitionsState() {
            _dbContext.LLMChannels.Add(new LLMChannel {
                Name = "vision-channel",
                Gateway = "https://example.com",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            });
            await _dbContext.SaveChangesAsync();

            _redisStore["ocrconf:100:state"] = OCRConfState.MainMenu.GetDescription();
            var mainMenuMarkup = await _service.GetReplyMarkupAsync(100);
            var mainMenuKeyboard = Assert.IsType<ReplyKeyboardMarkup>(mainMenuMarkup);
            Assert.Contains(mainMenuKeyboard.Keyboard, row => row.Any(button => button.Text == "切换OCR引擎"));

            var (switched, engineMessage) = await _service.ExecuteAsync("切换OCR引擎", 100);
            Assert.True(switched);
            Assert.Contains("请选择 OCR 引擎", engineMessage);

            _redisStore["ocrconf:100:state"] = OCRConfState.SelectingEngine.GetDescription();
            var engineMarkup = await _service.GetReplyMarkupAsync(100);
            var engineKeyboard = Assert.IsType<ReplyKeyboardMarkup>(engineMarkup);
            Assert.Contains(engineKeyboard.Keyboard, row => row.Any(button => button.Text == "PaddleOCR"));
            Assert.Contains(engineKeyboard.Keyboard, row => row.Any(button => button.Text == "LLM"));
        }
    }
}
