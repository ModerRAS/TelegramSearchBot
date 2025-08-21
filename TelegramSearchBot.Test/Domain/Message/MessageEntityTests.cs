using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Telegram.Bot.Types;
using TelegramSearchBot.Model.Data;
using Moq;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageEntityTests
    {
        /// <summary>
        /// 创建测试用的Telegram.Bot.Types.Message对象
        /// 简化实现：使用Moq框架来创建模拟对象，避免只读属性问题
        /// </summary>
        private static Telegram.Bot.Types.Message CreateTestTelegramMessage(int messageId, long chatId, long userId, string text, DateTime? date = null)
        {
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(messageId);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = chatId });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = userId });
            mockMessage.SetupGet(m => m.Text).Returns(text);
            mockMessage.SetupGet(m => m.Date).Returns(date ?? DateTime.UtcNow);
            
            return mockMessage.Object;
        }

        /// <summary>
        /// 创建测试用的Telegram.Bot.Types.Message对象（带回复消息）
        /// 简化实现：使用Moq框架来创建模拟对象，避免只读属性问题
        /// </summary>
        private static Telegram.Bot.Types.Message CreateTestTelegramMessageWithReply(int messageId, long chatId, long userId, string text, int replyToMessageId, long replyToUserId)
        {
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(messageId);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = chatId });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = userId });
            mockMessage.SetupGet(m => m.Text).Returns(text);
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var mockReplyMessage = new Mock<Telegram.Bot.Types.Message>();
            mockReplyMessage.SetupGet(m => m.MessageId).Returns(replyToMessageId);
            mockReplyMessage.SetupGet(m => m.From).Returns(new User { Id = replyToUserId });
            
            mockMessage.SetupGet(m => m.ReplyToMessage).Returns(mockReplyMessage.Object);
            
            return mockMessage.Object;
        }
        #region Constructor Tests

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

        #endregion

        #region FromTelegramMessage Tests

        [Fact]
        public void FromTelegramMessage_ValidTextMessage_ShouldCreateMessageCorrectly()
        {
            // Arrange
            var telegramMessage = CreateTestTelegramMessage(1000, 100, 1, "Hello World");

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

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
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(1001);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = 101 });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = 2 });
            mockMessage.SetupGet(m => m.Caption).Returns("Image caption");
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var telegramMessage = mockMessage.Object;

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

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
            var telegramMessage = CreateTestTelegramMessageWithReply(1002, 102, 3, "Reply message", 1001, 4);

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

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
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(1003);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = 103 });
            mockMessage.SetupGet(m => m.From).Returns((User)null);
            mockMessage.SetupGet(m => m.Text).Returns("Message without user");
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var telegramMessage = mockMessage.Object;

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(0, result.FromUserId);
        }

        [Fact]
        public void FromTelegramMessage_NullReplyToMessage_ShouldSetReplyToFieldsToZero()
        {
            // Arrange
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(1004);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = 104 });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = 5 });
            mockMessage.SetupGet(m => m.Text).Returns("Message without reply");
            mockMessage.SetupGet(m => m.ReplyToMessage).Returns((Telegram.Bot.Types.Message)null);
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var telegramMessage = mockMessage.Object;

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(0, result.ReplyToUserId);
            Assert.Equal(0, result.ReplyToMessageId);
        }

        [Fact]
        public void FromTelegramMessage_NullTextAndCaption_ShouldSetContentToEmpty()
        {
            // Arrange
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(1005);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = 105 });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = 6 });
            mockMessage.SetupGet(m => m.Text).Returns((string)null);
            mockMessage.SetupGet(m => m.Caption).Returns((string)null);
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var telegramMessage = mockMessage.Object;

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        #endregion

        #region Property Validation Tests

        [Fact]
        public void Message_Properties_ShouldSetAndGetCorrectly()
        {
            // Arrange
            var message = new TelegramSearchBot.Model.Data.Message();
            var testDateTime = DateTime.UtcNow;
            var testContent = "Test content";
            var testExtensions = new List<MessageExtension>
            {
                new MessageExtension { ExtensionType = "OCR", ExtensionData = "Test data" }
            };

            // Act
            message.DateTime = testDateTime;
            message.GroupId = 100;
            message.FromUserId = 1;
            message.ReplyToUserId = 2;
            message.ReplyToMessageId = 999;
            message.Content = testContent;
            message.MessageExtensions = testExtensions;

            // Assert
            // Id是由数据库生成的，所以验证默认值
            Assert.Equal(0, message.Id);
            Assert.Equal(testDateTime, message.DateTime);
            Assert.Equal(100, message.GroupId);
            Assert.Equal(0, message.MessageId); // MessageId需要通过FromTelegramMessage设置
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
            var message = new TelegramSearchBot.Model.Data.Message();

            // Act & Assert
            Assert.NotNull(message.MessageExtensions);
            Assert.Empty(message.MessageExtensions);
        }

        [Fact]
        public void Message_MessageExtensions_ShouldAllowAddingExtensions()
        {
            // Arrange
            var message = new TelegramSearchBot.Model.Data.Message();
            var extension = new MessageExtension { ExtensionType = "OCR", ExtensionData = "Test data" };

            // Act
            message.MessageExtensions.Add(extension);

            // Assert
            Assert.Single(message.MessageExtensions);
            // 简化实现：原本实现是使用索引访问message.MessageExtensions[0]
            // 简化实现：改为使用LINQ的First()方法，因为ICollection不支持索引访问
            Assert.Same(extension, message.MessageExtensions.First());
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void FromTelegramMessage_EmptyText_ShouldCreateMessageWithEmptyContent()
        {
            // Arrange
            var telegramMessage = CreateTestTelegramMessage(1006, 106, 7, "");

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        [Fact]
        public void FromTelegramMessage_EmptyCaption_ShouldCreateMessageWithEmptyContent()
        {
            // Arrange
            var mockMessage = new Mock<Telegram.Bot.Types.Message>();
            mockMessage.SetupGet(m => m.MessageId).Returns(1007);
            mockMessage.SetupGet(m => m.Chat).Returns(new Chat { Id = 107 });
            mockMessage.SetupGet(m => m.From).Returns(new User { Id = 8 });
            mockMessage.SetupGet(m => m.Caption).Returns("");
            mockMessage.SetupGet(m => m.Date).Returns(DateTime.UtcNow);
            
            var telegramMessage = mockMessage.Object;

            // Act
            var result = TelegramSearchBot.Model.Data.Message.FromTelegramMessage(telegramMessage);

            // Assert
            Assert.Equal(string.Empty, result.Content);
        }

        #endregion
    }
}