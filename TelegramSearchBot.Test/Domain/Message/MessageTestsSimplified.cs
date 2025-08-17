using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Tests.Message;

namespace TelegramSearchBot.Domain.Tests.Message
{
    public class MessageTestsSimplified
    {
        [Fact]
        public void MessageTestDataFactory_CreateValidMessage_ShouldReturnValidMessage()
        {
            // Arrange
            long groupId = 100L;
            long messageId = 1000L;
            
            // Act
            var message = MessageTestDataFactory.CreateValidMessage(groupId, messageId);
            
            // Assert
            Assert.NotNull(message);
            Assert.Equal(groupId, message.GroupId);
            Assert.Equal(messageId, message.MessageId);
            Assert.NotEmpty(message.Content);
            Assert.True(message.DateTime > DateTime.MinValue);
        }

        [Fact]
        public void MessageTestDataFactory_CreateMessageExtension_ShouldReturnValidExtension()
        {
            // Arrange
            long messageId = 1000L;
            string type = "OCR";
            string value = "Extracted text";
            
            // Act
            var extension = MessageTestDataFactory.CreateMessageExtension(messageId, type, value);
            
            // Assert
            Assert.NotNull(extension);
            Assert.Equal(messageId, extension.MessageId);
            Assert.Equal(type, extension.Type);
            Assert.Equal(value, extension.Value);
            Assert.True(extension.CreatedAt > DateTime.MinValue);
        }

        [Fact]
        public void MessageTestDataFactory_CreateUserData_ShouldReturnValidUserData()
        {
            // Arrange
            long userId = 1L;
            
            // Act
            var userData = MessageTestDataFactory.CreateUserData(userId);
            
            // Assert
            Assert.NotNull(userData);
            Assert.Equal(userId, userData.Id);
            Assert.NotNull(userData.FirstName);
        }

        [Fact]
        public void MessageTestDataFactory_CreateGroupData_ShouldReturnValidGroupData()
        {
            // Arrange
            long groupId = 100L;
            
            // Act
            var groupData = MessageTestDataFactory.CreateGroupData(groupId);
            
            // Assert
            Assert.NotNull(groupData);
            Assert.Equal(groupId, groupData.Id);
            Assert.NotNull(groupData.Title);
        }

        [Fact]
        public void MessageTestDataFactory_CreateUserWithGroup_ShouldReturnValidUserWithGroup()
        {
            // Arrange
            long userId = 1L;
            long groupId = 100L;
            
            // Act
            var userWithGroup = MessageTestDataFactory.CreateUserWithGroup(userId, groupId);
            
            // Assert
            Assert.NotNull(userWithGroup);
            Assert.Equal(userId, userWithGroup.UserId);
            Assert.Equal(groupId, userWithGroup.GroupId);
        }

        [Fact]
        public void MessageTestDataFactory_CreateValidMessageOption_ShouldReturnValidMessageOption()
        {
            // Arrange
            long userId = 1L;
            long chatId = 100L;
            long messageId = 1000L;
            string content = "Test message";
            
            // Act
            var messageOption = MessageTestDataFactory.CreateValidMessageOption(userId, chatId, messageId, content);
            
            // Assert
            Assert.NotNull(messageOption);
            Assert.Equal(userId, messageOption.UserId);
            Assert.Equal(chatId, messageOption.ChatId);
            Assert.Equal(messageId, messageOption.MessageId);
            Assert.Equal(content, messageOption.Content);
            Assert.NotNull(messageOption.User);
            Assert.NotNull(messageOption.Chat);
        }

        [Fact]
        public void MessageTestDataFactory_CreateMessageWithReply_ShouldReturnValidMessageOption()
        {
            // Arrange
            long userId = 1L;
            long chatId = 100L;
            long messageId = 1001L;
            string content = "Reply message";
            long replyToMessageId = 1000L;
            
            // Act
            var messageOption = MessageTestDataFactory.CreateMessageWithReply(userId, chatId, messageId, content, replyToMessageId);
            
            // Assert
            Assert.NotNull(messageOption);
            Assert.Equal(userId, messageOption.UserId);
            Assert.Equal(chatId, messageOption.ChatId);
            Assert.Equal(messageId, messageOption.MessageId);
            Assert.Equal(content, messageOption.Content);
            Assert.Equal(replyToMessageId, messageOption.ReplyTo);
        }

        [Fact]
        public void MessageTestDataFactory_CreateLongMessage_ShouldReturnLongMessageOption()
        {
            // Arrange
            int wordCount = 100;
            
            // Act
            var messageOption = MessageTestDataFactory.CreateLongMessage(wordCount);
            
            // Assert
            Assert.NotNull(messageOption);
            Assert.True(messageOption.Content.Length > 500);
            Assert.Contains($"Long message with {wordCount} words", messageOption.Content);
        }

        [Fact]
        public void MessageTestDataFactory_CreateMessageWithSpecialChars_ShouldReturnValidMessageOption()
        {
            // Act
            var messageOption = MessageTestDataFactory.CreateMessageWithSpecialChars();
            
            // Assert
            Assert.NotNull(messageOption);
            Assert.Contains("中文", messageOption.Content);
            Assert.Contains("😊", messageOption.Content);
            Assert.Contains("Special", messageOption.Content);
        }

        [Fact]
        public void MessageExtension_WithMessageId_ShouldSetMessageId()
        {
            // Arrange
            var extension = MessageTestDataFactory.CreateMessageExtension(1000L, "Test", "Value");
            long newMessageId = 2000L;
            
            // Act
            var result = extension.WithMessageId(newMessageId);
            
            // Assert
            Assert.Equal(newMessageId, result.MessageId);
        }

        [Fact]
        public void MessageExtension_WithType_ShouldSetType()
        {
            // Arrange
            var extension = MessageTestDataFactory.CreateMessageExtension(1000L, "Test", "Value");
            string newType = "NewType";
            
            // Act
            var result = extension.WithType(newType);
            
            // Assert
            Assert.Equal(newType, result.Type);
        }

        [Fact]
        public void MessageExtension_WithValue_ShouldSetValue()
        {
            // Arrange
            var extension = MessageTestDataFactory.CreateMessageExtension(1000L, "Test", "Value");
            string newValue = "NewValue";
            
            // Act
            var result = extension.WithValue(newValue);
            
            // Assert
            Assert.Equal(newValue, result.Value);
        }

        [Fact]
        public void MessageExtension_WithCreatedAt_ShouldSetCreatedAt()
        {
            // Arrange
            var extension = MessageTestDataFactory.CreateMessageExtension(1000L, "Test", "Value");
            DateTime newCreatedAt = DateTime.UtcNow;
            
            // Act
            var result = extension.WithCreatedAt(newCreatedAt);
            
            // Assert
            Assert.Equal(newCreatedAt, result.CreatedAt);
        }

        [Fact]
        public void Message_WithGroupId_ShouldSetGroupId()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            long newGroupId = 200L;
            
            // Act
            var result = message.WithGroupId(newGroupId);
            
            // Assert
            Assert.Equal(newGroupId, result.GroupId);
        }

        [Fact]
        public void Message_WithMessageId_ShouldSetMessageId()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            long newMessageId = 2000L;
            
            // Act
            var result = message.WithMessageId(newMessageId);
            
            // Assert
            Assert.Equal(newMessageId, result.MessageId);
        }

        [Fact]
        public void Message_WithFromUserId_ShouldSetFromUserId()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            long newFromUserId = 2L;
            
            // Act
            var result = message.WithFromUserId(newFromUserId);
            
            // Assert
            Assert.Equal(newFromUserId, result.FromUserId);
        }

        [Fact]
        public void Message_WithContent_ShouldSetContent()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            string newContent = "New content";
            
            // Act
            var result = message.WithContent(newContent);
            
            // Assert
            Assert.Equal(newContent, result.Content);
        }

        [Fact]
        public void Message_WithDateTime_ShouldSetDateTime()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            DateTime newDateTime = DateTime.UtcNow;
            
            // Act
            var result = message.WithDateTime(newDateTime);
            
            // Assert
            Assert.Equal(newDateTime, result.DateTime);
        }

        [Fact]
        public void Message_WithReplyToMessageId_ShouldSetReplyToMessageId()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            long newReplyToMessageId = 2000L;
            
            // Act
            var result = message.WithReplyToMessageId(newReplyToMessageId);
            
            // Assert
            Assert.Equal(newReplyToMessageId, result.ReplyToMessageId);
        }

        [Fact]
        public void Message_WithReplyToUserId_ShouldSetReplyToUserId()
        {
            // Arrange
            var message = MessageTestDataFactory.CreateValidMessage(100L, 1000L);
            long newReplyToUserId = 2L;
            
            // Act
            var result = message.WithReplyToUserId(newReplyToUserId);
            
            // Assert
            Assert.Equal(newReplyToUserId, result.ReplyToUserId);
        }
    }
}