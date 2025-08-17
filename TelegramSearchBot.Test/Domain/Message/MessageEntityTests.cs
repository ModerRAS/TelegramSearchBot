using System;
using System.Collections.Generic;
using Xunit;
using Telegram.Bot.Types;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageEntityTests
    {
        #region Constructor Tests

        [Fact]
        public void Message_Constructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var message = new Message();

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

        #endregion

        #region FromTelegramMessage Tests

        [Fact]
        public void FromTelegramMessage_ValidTextMessage_ShouldCreateMessageCorrectly()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1000,
                Chat = new Chat { Id = 100 },
                From = new User { Id = 1 },
                Text = "Hello World",
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(telegramMessage.MessageId, result.MessageId);
            Assert.Equal(telegramMessage.Chat.Id, result.GroupId);
            Assert.Equal(telegramMessage.From.Id, result.FromUserId);
            Assert.Equal(telegramMessage.Text, result.Content);
            Assert.Equal(telegramMessage.Date, result.DateTime);
            Assert.Equal(0, result.ReplyToUserId);
            Assert.Equal(0, result.ReplyToMessageId);
        }

        [Fact]
        public void FromTelegramMessage_ValidCaptionMessage_ShouldUseCaptionAsContent()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1001,
                Chat = new Chat { Id = 101 },
                From = new User { Id = 2 },
                Caption = "Image caption",
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(telegramMessage.MessageId, result.MessageId);
            Assert.Equal(telegramMessage.Chat.Id, result.GroupId);
            Assert.Equal(telegramMessage.From.Id, result.FromUserId);
            Assert.Equal(telegramMessage.Caption, result.Content);
            Assert.Equal(telegramMessage.Date, result.DateTime);
        }

        [Fact]
        public void FromTelegramMessage_WithReplyToMessage_ShouldSetReplyToFields()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1002,
                Chat = new Chat { Id = 102 },
                From = new User { Id = 3 },
                Text = "Reply message",
                ReplyToMessage = new Telegram.Bot.Types.Message
                {
                    MessageId = 1001,
                    From = new User { Id = 4 }
                },
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(telegramMessage.MessageId, result.MessageId);
            Assert.Equal(telegramMessage.Chat.Id, result.GroupId);
            Assert.Equal(telegramMessage.From.Id, result.FromUserId);
            Assert.Equal(telegramMessage.ReplyToMessage.From.Id, result.ReplyToUserId);
            Assert.Equal(telegramMessage.ReplyToMessage.MessageId, result.ReplyToMessageId);
            Assert.Equal(telegramMessage.Text, result.Content);
        }

        [Fact]
        public void FromTelegramMessage_NullFromUser_ShouldSetUserIdToZero()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1003,
                Chat = new Chat { Id = 103 },
                From = null,
                Text = "Message without user",
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(0, result.FromUserId);
        }

        [Fact]
        public void FromTelegramMessage_NullReplyToMessage_ShouldSetReplyToFieldsToZero()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1004,
                Chat = new Chat { Id = 104 },
                From = new User { Id = 5 },
                Text = "Message without reply",
                ReplyToMessage = null,
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(0, result.ReplyToUserId);
            Assert.Equal(0, result.ReplyToMessageId);
        }

        [Fact]
        public void FromTelegramMessage_NullTextAndCaption_ShouldSetContentToEmpty()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1005,
                Chat = new Chat { Id = 105 },
                From = new User { Id = 6 },
                Text = null,
                Caption = null,
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        #endregion

        #region Property Validation Tests

        [Fact]
        public void Message_Properties_ShouldSetAndGetCorrectly()
        {
            // Arrange
            var message = new Message();
            var testDateTime = DateTime.UtcNow;
            var testContent = "Test content";
            var testExtensions = new List<MessageExtension>
            {
                new MessageExtension { ExtensionType = "OCR", ExtensionData = "Test data" }
            };

            // Act
            message.Id = 1;
            message.DateTime = testDateTime;
            message.GroupId = 100;
            message.MessageId = 1000;
            message.FromUserId = 1;
            message.ReplyToUserId = 2;
            message.ReplyToMessageId = 999;
            message.Content = testContent;
            message.MessageExtensions = testExtensions;

            // Assert
            Assert.Equal(1, message.Id);
            Assert.Equal(testDateTime, message.DateTime);
            Assert.Equal(100, message.GroupId);
            Assert.Equal(1000, message.MessageId);
            Assert.Equal(1, message.FromUserId);
            Assert.Equal(2, message.ReplyToUserId);
            Assert.Equal(999, message.ReplyToMessageId);
            Assert.Equal(testContent, message.Content);
            Assert.Same(testExtensions, message.MessageExtensions);
        }

        [Fact]
        public void Message_MessageExtensions_ShouldInitializeEmptyCollection()
        {
            // Arrange
            var message = new Message();

            // Act & Assert
            Assert.NotNull(message.MessageExtensions);
            Assert.Empty(message.MessageExtensions);
        }

        [Fact]
        public void Message_MessageExtensions_ShouldAllowAddingExtensions()
        {
            // Arrange
            var message = new Message();
            var extension = new MessageExtension { ExtensionType = "OCR", ExtensionData = "Test data" };

            // Act
            message.MessageExtensions.Add(extension);

            // Assert
            Assert.Single(message.MessageExtensions);
            Assert.Same(extension, message.MessageExtensions[0]);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void FromTelegramMessage_EmptyText_ShouldCreateMessageWithEmptyContent()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1006,
                Chat = new Chat { Id = 106 },
                From = new User { Id = 7 },
                Text = "",
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        [Fact]
        public void FromTelegramMessage_EmptyCaption_ShouldCreateMessageWithEmptyContent()
        {
            // Arrange
            var telegramMessage = new Telegram.Bot.Types.Message
            {
                MessageId = 1007,
                Chat = new Chat { Id = 107 },
                From = new User { Id = 8 },
                Caption = "",
                Date = DateTime.UtcNow
            };

            // Act
            var result = Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        #endregion
    }
}