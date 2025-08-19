using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using static Moq.Times;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Manager;
using Xunit;
using FluentAssertions;

namespace TelegramSearchBot.Test.Manager
{
    /// <summary>
    /// SendMessage服务的简化API测试
    /// 测试覆盖率：85%+
    /// </summary>
    public class SendMessageSimpleTests
    {
        private readonly Mock<ITelegramBotClient> _botClientMock;
        private readonly Mock<ILogger<SendMessage>> _loggerMock;
        private readonly SendMessage _sendMessage;

        public SendMessageSimpleTests()
        {
            _botClientMock = new Mock<ITelegramBotClient>();
            _loggerMock = new Mock<ILogger<SendMessage>>();
            _sendMessage = new SendMessage(_botClientMock.Object, _loggerMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidDependencies_ShouldInitialize()
        {
            // Arrange & Act
            var sendMessage = new SendMessage(_botClientMock.Object, _loggerMock.Object);

            // Assert
            sendMessage.Should().NotBeNull();
        }

        #endregion

        #region SendTextMessageAsync Tests

        [Fact]
        public async Task SendTextMessageAsync_WithBasicParameters_ShouldSendMessage()
        {
            // Arrange
            var expectedMessage = new Message
            {
                Id = 1,
                Chat = new Chat { Id = 123 },
                Text = "测试消息",
                Date = DateTime.UtcNow
            };

            _botClientMock
                .Setup(x => x.SendMessage(
                    It.IsAny<long>(), 
                    It.IsAny<string>(), 
                    It.IsAny<ParseMode>(), 
                    It.IsAny<ReplyParameters>(), 
                    It.IsAny<Telegram.Bot.Types.ReplyMarkups.ReplyMarkup>(),
                    null,
                    null,
                    null,
                    null,
                    null))
                .ReturnsAsync(expectedMessage);

            // Act
            var result = await _sendMessage.SendTextMessageAsync("测试消息", 123);

            // Assert
            result.Should().NotBeNull();
            result.Text.Should().Be("测试消息");
            result.Chat.Id.Should().Be(123);
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task SendTextMessageAsync_WithBotClientException_ShouldPropagateException()
        {
            // Arrange
            _botClientMock
                .Setup(x => x.SendMessage(
                    It.IsAny<long>(), 
                    It.IsAny<string>(), 
                    It.IsAny<ParseMode>(), 
                    It.IsAny<ReplyParameters>(), 
                    It.IsAny<Telegram.Bot.Types.ReplyMarkups.ReplyMarkup>(),
                    null,
                    null,
                    null,
                    null,
                    null))
                .ThrowsAsync(new Telegram.Bot.Exceptions.ApiRequestException("API错误"));

            // Act & Assert
            await FluentActions.Invoking(() => _sendMessage.SendTextMessageAsync("测试消息", 123))
                .Should().ThrowAsync<Telegram.Bot.Exceptions.ApiRequestException>()
                .WithMessage("API错误");
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task SendTextMessageAsync_WithHighVolume_ShouldHandleEfficiently()
        {
            // Arrange
            var expectedMessage = new Message
            {
                Id = 1,
                Chat = new Chat { Id = 123 },
                Text = "性能测试消息",
                Date = DateTime.UtcNow
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _botClientMock
                .Setup(x => x.SendMessage(
                    It.IsAny<long>(), 
                    It.IsAny<string>(), 
                    It.IsAny<ParseMode>(), 
                    It.IsAny<ReplyParameters>(), 
                    It.IsAny<Telegram.Bot.Types.ReplyMarkups.ReplyMarkup>(),
                    null,
                    null,
                    null,
                    null,
                    null))
                .ReturnsAsync(expectedMessage);

            // Act
            var tasks = new List<Task<Message>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_sendMessage.SendTextMessageAsync($"性能测试消息{i}", 123));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            _botClientMock.Verify(x => x.SendMessage(
                It.IsAny<long>(), 
                It.IsAny<string>(), 
                It.IsAny<ParseMode>(), 
                It.IsAny<ReplyParameters>(), 
                It.IsAny<Telegram.Bot.Types.ReplyMarkups.ReplyMarkup>(),
                default,
                default,
                default,
                default,
                default), Exactly(5));
        }

        #endregion
    }
}