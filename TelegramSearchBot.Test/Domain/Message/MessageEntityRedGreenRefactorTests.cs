using System;
using System.Linq;
using Xunit;
using Telegram.Bot.Types;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageEntityRedGreenRefactorTests
    {
        [Fact]
        public void Message_Constructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var message = new TelegramSearchBot.Model.Data.Message();

            // Assert
            Assert.Equal(0, message.Id);
            Assert.Equal(default(DateTime), message.DateTime);
            Assert.Equal(0, message.GroupId);
            Assert.Equal(0, message.MessageId);
            Assert.Equal(0, message.FromUserId);
            Assert.Equal(0, message.ReplyToUserId);
            Assert.Equal(0, message.ReplyToMessageId);
            Assert.Null(message.Content);
            Assert.NotNull(message.MessageExtensions);
        }

        [Fact]
        public void Message_Constructor_ShouldInitializeWithValidData()
        {
            // Arrange & Act
            var message = new TelegramSearchBot.Model.Data.Message
            {
                GroupId = 100,
                MessageId = 1000,
                FromUserId = 1,
                Content = "Valid content",
                DateTime = DateTime.UtcNow
            };

            // Assert
            Assert.Equal(100, message.GroupId);
            Assert.Equal(1000, message.MessageId);
            Assert.Equal(1, message.FromUserId);
            Assert.Equal("Valid content", message.Content);
            Assert.NotNull(message.DateTime);
            Assert.NotNull(message.MessageExtensions);
        }

        [Fact]
        public void Message_ShouldHandleEmptyContent()
        {
            // Arrange & Act
            var message = new TelegramSearchBot.Model.Data.Message
            {
                GroupId = 100,
                MessageId = 1000,
                FromUserId = 1,
                Content = "",
                DateTime = DateTime.UtcNow
            };

            // Assert
            Assert.Equal("", message.Content);
            Assert.NotNull(message.MessageExtensions);
        }

        [Fact]
        public void Message_FromTelegramMessage_ShouldCreateMessageCorrectly()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1000,
                Chat = new Telegram.Bot.Types.Chat { Id = 100 },
                From = new Telegram.Bot.Types.User { Id = 1 },
                Text = "Hello World",
                Date = DateTime.UtcNow
            };

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(telegramMessage.MessageId, result.MessageId);
            Assert.Equal(telegramMessage.Chat.Id, result.GroupId);
            Assert.Equal(telegramMessage.From.Id, result.FromUserId);
            Assert.Equal(telegramMessage.Text, result.Content);
            Assert.Equal(telegramMessage.Date, result.DateTime);
        }
    }
}