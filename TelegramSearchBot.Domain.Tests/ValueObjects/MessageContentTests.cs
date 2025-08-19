using Xunit;
using TelegramSearchBot.Domain.Message.ValueObjects;
using System;

namespace TelegramSearchBot.Domain.Tests.ValueObjects
{
    /// <summary>
    /// MessageContent值对象的单元测试
    /// 测试DDD架构中值对象的内容验证、清理和业务规则
    /// </summary>
    public class MessageContentTests
    {
        [Fact]
        public void Constructor_WithValidContent_ShouldCreateMessageContent()
        {
            // Arrange
            string content = "这是一条测试消息";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal(content, messageContent.Value);
            Assert.Equal(content.Length, messageContent.Length);
            Assert.False(messageContent.IsEmpty);
        }

        [Fact]
        public void Constructor_WithNullContent_ShouldThrowArgumentException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageContent(null));
            
            Assert.Contains("Content cannot be null", exception.Message);
        }

        [Fact]
        public void Constructor_WithEmptyContent_ShouldCreateEmptyMessageContent()
        {
            // Arrange
            string content = "";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal(content, messageContent.Value);
            Assert.Equal(0, messageContent.Length);
            Assert.True(messageContent.IsEmpty);
        }

        [Fact]
        public void Constructor_WithWhitespaceContent_ShouldTrimContent()
        {
            // Arrange
            string content = "  测试消息  ";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal("测试消息", messageContent.Value);
        }

        [Fact]
        public void Constructor_WithControlCharacters_ShouldRemoveControlCharacters()
        {
            // Arrange
            string content = "测试\u0001消息\u0002";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal("测试消息", messageContent.Value);
        }

        [Fact]
        public void Constructor_WithMixedLineBreaks_ShouldNormalizeLineBreaks()
        {
            // Arrange
            string content = "第一行\r\n第二行\r第三行\n第四行";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal("第一行\n第二行\n第三行\n第四行", messageContent.Value);
        }

        [Fact]
        public void Constructor_WithMultipleLineBreaks_ShouldCompressLineBreaks()
        {
            // Arrange
            string content = "第一行\n\n\n\n第二行";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal("第一行\n\n第二行", messageContent.Value);
        }

        [Fact]
        public void Constructor_WithContentExceedingMaxLength_ShouldThrowArgumentException()
        {
            // Arrange
            string content = new string('A', 5001);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                new MessageContent(content));
            
            Assert.Contains("Content length cannot exceed 5000 characters", exception.Message);
        }

        [Fact]
        public void Constructor_WithContentAtMaxLength_ShouldCreateMessageContent()
        {
            // Arrange
            string content = new string('A', 5000);

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal(content, messageContent.Value);
            Assert.Equal(5000, messageContent.Length);
        }

        [Fact]
        public void Equals_WithSameContent_ShouldReturnTrue()
        {
            // Arrange
            var content1 = new MessageContent("测试消息");
            var content2 = new MessageContent("测试消息");

            // Act & Assert
            Assert.Equal(content1, content2);
            Assert.True(content1 == content2);
            Assert.False(content1 != content2);
        }

        [Fact]
        public void Equals_WithDifferentContent_ShouldReturnFalse()
        {
            // Arrange
            var content1 = new MessageContent("消息1");
            var content2 = new MessageContent("消息2");

            // Act & Assert
            Assert.NotEqual(content1, content2);
            Assert.True(content1 != content2);
            Assert.False(content1 == content2);
        }

        [Fact]
        public void Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("测试消息");

            // Act & Assert
            Assert.False(content.Equals(null));
        }

        [Fact]
        public void GetHashCode_WithSameContent_ShouldReturnSameHashCode()
        {
            // Arrange
            var content1 = new MessageContent("测试消息");
            var content2 = new MessageContent("测试消息");

            // Act & Assert
            Assert.Equal(content1.GetHashCode(), content2.GetHashCode());
        }

        [Fact]
        public void ToString_ShouldReturnValue()
        {
            // Arrange
            var content = new MessageContent("测试消息");

            // Act
            var result = content.ToString();

            // Assert
            Assert.Equal("测试消息", result);
        }

        [Fact]
        public void Empty_ShouldReturnEmptyMessageContent()
        {
            // Act & Assert
            var emptyContent = MessageContent.Empty;
            Assert.Equal("", emptyContent.Value);
            Assert.Equal(0, emptyContent.Length);
            Assert.True(emptyContent.IsEmpty);
        }

        [Fact]
        public void Trim_ShouldReturnTrimmedContent()
        {
            // Arrange
            var content = new MessageContent("  测试消息  ");

            // Act
            var trimmed = content.Trim();

            // Assert
            Assert.Equal("测试消息", trimmed.Value);
            Assert.NotEqual(content, trimmed);
        }

        [Fact]
        public void Substring_WithValidParameters_ShouldReturnSubstring()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var substring = content.Substring(2, 4);

            // Assert
            Assert.Equal("一条测试", substring.Value);
        }

        [Fact]
        public void Substring_WithInvalidStartIndex_ShouldThrowArgumentException()
        {
            // Arrange
            var content = new MessageContent("测试消息");

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                content.Substring(-1, 2));
            
            Assert.Contains("Start index is out of range", exception.Message);
        }

        [Fact]
        public void Substring_WithInvalidLength_ShouldThrowArgumentException()
        {
            // Arrange
            var content = new MessageContent("测试消息");

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                content.Substring(0, 10));
            
            Assert.Contains("must refer to a location within the string", exception.Message);
        }

        [Fact]
        public void Contains_WithExistingText_ShouldReturnTrue()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.Contains("测试");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Contains_WithNonExistingText_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.Contains("不存在");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Contains_WithNullText_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.Contains(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void StartsWith_WithMatchingPrefix_ShouldReturnTrue()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.StartsWith("这是");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void StartsWith_WithNonMatchingPrefix_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.StartsWith("不是");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void StartsWith_WithNullText_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.StartsWith(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EndsWith_WithMatchingSuffix_ShouldReturnTrue()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.EndsWith("消息");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EndsWith_WithNonMatchingSuffix_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.EndsWith("不是");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EndsWith_WithNullText_ShouldReturnFalse()
        {
            // Arrange
            var content = new MessageContent("这是一条测试消息");

            // Act
            var result = content.EndsWith(null);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("\r")]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public void Constructor_WithVariousWhitespace_ShouldHandleCorrectly(string input)
        {
            // Act
            var content = new MessageContent(input);

            // Assert
            Assert.Equal(input.Trim(), content.Value);
        }

        [Fact]
        public void Constructor_WithSpecialCharacters_ShouldPreserveSpecialCharacters()
        {
            // Arrange
            string content = "消息包含特殊字符：@#$%^&*()_+-=[]{}|;':\",./<>?";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal(content, messageContent.Value);
        }

        [Fact]
        public void Constructor_WithUnicodeCharacters_ShouldPreserveUnicode()
        {
            // Arrange
            string content = "测试消息包含Unicode：🌟😊🎉";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            Assert.Equal(content, messageContent.Value);
        }
    }
}