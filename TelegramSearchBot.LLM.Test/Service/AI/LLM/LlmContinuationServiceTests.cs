using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class LlmContinuationServiceTests {
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<ILogger<LlmContinuationService>> _loggerMock;
        private readonly LlmContinuationService _service;

        public LlmContinuationServiceTests() {
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _loggerMock = new Mock<ILogger<LlmContinuationService>>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
            _service = new LlmContinuationService(_redisMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task SaveSnapshotAsync_SetsValueInRedis_ReturnsId() {
            // Arrange
            var snapshot = new LlmContinuationSnapshot {
                ChatId = 12345,
                OriginalMessageId = 100,
                UserId = 9999,
                ModelName = "gpt-4o",
                Provider = "OpenAI",
                ChannelId = 1,
                LastAccumulatedContent = "Hello world",
                CyclesSoFar = 25,
                ProviderHistory = new List<SerializedChatMessage> {
                    new SerializedChatMessage { Role = "system", Content = "You are helpful" },
                    new SerializedChatMessage { Role = "user", Content = "Hi" },
                    new SerializedChatMessage { Role = "assistant", Content = "Hello!" },
                }
            };

            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            // Also handle 4-param overload
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var snapshotId = await _service.SaveSnapshotAsync(snapshot);

            // Assert
            Assert.NotNull(snapshotId);
            Assert.NotEmpty(snapshotId);
            Assert.Equal(snapshotId, snapshot.SnapshotId);
        }

        [Fact]
        public async Task SaveSnapshotAsync_NullSnapshot_ThrowsArgumentNullException() {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveSnapshotAsync(null));
        }

        [Fact]
        public async Task GetSnapshotAsync_NullOrEmptyId_ReturnsNull() {
            Assert.Null(await _service.GetSnapshotAsync(null));
            Assert.Null(await _service.GetSnapshotAsync(""));
        }

        [Fact]
        public async Task GetSnapshotAsync_NotFound_ReturnsNull() {
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var result = await _service.GetSnapshotAsync("nonexistent");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSnapshotAsync_ValidJson_DeserializesCorrectly() {
            var snapshot = new LlmContinuationSnapshot {
                SnapshotId = "test123",
                ChatId = 12345,
                OriginalMessageId = 100,
                ModelName = "gpt-4o",
                Provider = "OpenAI",
                ChannelId = 1,
                LastAccumulatedContent = "Test content",
                CyclesSoFar = 25,
                ProviderHistory = new List<SerializedChatMessage> {
                    new SerializedChatMessage { Role = "system", Content = "System prompt" },
                    new SerializedChatMessage { Role = "user", Content = "Hello" },
                    new SerializedChatMessage { Role = "assistant", Content = "Hi there!" },
                    new SerializedChatMessage { Role = "user", Content = "[Tool 'bash' result: done]" },
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot);
            _dbMock.Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains("test123")), It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(json));

            var result = await _service.GetSnapshotAsync("test123");

            Assert.NotNull(result);
            Assert.Equal("test123", result.SnapshotId);
            Assert.Equal(12345, result.ChatId);
            Assert.Equal("gpt-4o", result.ModelName);
            Assert.Equal("OpenAI", result.Provider);
            Assert.Equal("Test content", result.LastAccumulatedContent);
            Assert.Equal(25, result.CyclesSoFar);
            Assert.Equal(4, result.ProviderHistory.Count);
            Assert.Equal("system", result.ProviderHistory[0].Role);
            Assert.Equal("user", result.ProviderHistory[1].Role);
            Assert.Equal("assistant", result.ProviderHistory[2].Role);
            Assert.Equal("[Tool 'bash' result: done]", result.ProviderHistory[3].Content);
        }

        [Fact]
        public async Task GetSnapshotAsync_InvalidJson_ReturnsNull() {
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue("not valid json {{{"));

            var result = await _service.GetSnapshotAsync("bad");
            Assert.Null(result);
        }

        [Fact]
        public async Task DeleteSnapshotAsync_CallsRedisDelete() {
            _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            await _service.DeleteSnapshotAsync("to-delete");

            _dbMock.Verify(d => d.KeyDeleteAsync(
                It.Is<RedisKey>(k => k.ToString().Contains("to-delete")),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task DeleteSnapshotAsync_NullId_DoesNothing() {
            await _service.DeleteSnapshotAsync(null);
            await _service.DeleteSnapshotAsync("");
            _dbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task TryAcquireLockAsync_Success_ReturnsTrue() {
            // Mock all overloads of StringSetAsync that might be called
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
                .ReturnsAsync(true);

            var result = await _service.TryAcquireLockAsync("snap1");
            Assert.True(result);
        }

        [Fact]
        public async Task TryAcquireLockAsync_AlreadyLocked_ReturnsFalse() {
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
                .ReturnsAsync(false);

            var result = await _service.TryAcquireLockAsync("snap1");
            Assert.False(result);
        }

        [Fact]
        public async Task TryAcquireLockAsync_NullId_ReturnsFalse() {
            var result = await _service.TryAcquireLockAsync(null);
            Assert.False(result);
        }

        [Fact]
        public async Task ReleaseLockAsync_CallsRedisDelete() {
            _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            await _service.ReleaseLockAsync("snap1");

            _dbMock.Verify(d => d.KeyDeleteAsync(
                It.Is<RedisKey>(k => k.ToString().Contains("snap1")),
                It.IsAny<CommandFlags>()), Times.Once);
        }
    }
}
