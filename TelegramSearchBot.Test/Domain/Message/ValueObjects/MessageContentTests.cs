using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Tests.Message.ValueObjects
{
    public class MessageContentTests
    {
        #region Constructor Tests

        [Fact]
        public void MessageContent_Constructor_WithValidContent_ShouldCreateMessageContent()
        {
            // Arrange
            var content = "Hello World";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be(content);
            messageContent.Length.Should().Be(content.Length);
            messageContent.IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void MessageContent_Constructor_WithEmptyContent_ShouldCreateEmptyMessageContent()
        {
            // Arrange
            var content = "";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be(content);
            messageContent.Length.Should().Be(0);
            messageContent.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void MessageContent_Constructor_WithNullContent_ShouldThrowArgumentException()
        {
            // Arrange
            string content = null;

            // Act
            var action = () => new MessageContent(content);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Content cannot be null");
        }

        [Fact]
        public void MessageContent_Constructor_WithContentTooLong_ShouldThrowArgumentException()
        {
            // Arrange
            var content = new string('a', 5001); // 超过5000字符限制

            // Act
            var action = () => new MessageContent(content);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Content length cannot exceed 5000 characters");
        }

        [Fact]
        public void MessageContent_Constructor_WithExactlyMaxLength_ShouldCreateMessageContent()
        {
            // Arrange
            var content = new string('a', 5000); // 正好5000字符

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be(content);
            messageContent.Length.Should().Be(5000);
        }

        #endregion

        #region Content Cleaning Tests

        [Fact]
        public void MessageContent_Constructor_ShouldTrimWhitespace()
        {
            // Arrange
            var content = "  Hello World  ";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be("Hello World");
        }

        [Fact]
        public void MessageContent_Constructor_ShouldNormalizeLineBreaks()
        {
            // Arrange
            var content = "Line1\r\nLine2\rLine3\nLine4";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be("Line1\nLine2\nLine3\nLine4");
        }

        [Fact]
        public void MessageContent_Constructor_ShouldRemoveControlCharacters()
        {
            // Arrange
            var content = "Hello\x00World\x01Test\x02";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be("HelloWorldTest");
        }

        [Fact]
        public void MessageContent_Constructor_ShouldCompressMultipleLineBreaks()
        {
            // Arrange
            var content = "Line1\n\n\nLine2\n\n\n\nLine3";

            // Act
            var messageContent = new MessageContent(content);

            // Assert
            messageContent.Value.Should().Be("Line1\n\nLine2\n\nLine3");
        }

        #endregion

        #region Equality Tests

        [Fact]
        public void MessageContent_Equals_WithSameContent_ShouldBeEqual()
        {
            // Arrange
            var content = "Hello World";
            var messageContent1 = new MessageContent(content);
            var messageContent2 = new MessageContent(content);

            // Act & Assert
            messageContent1.Should().Be(messageContent2);
            messageContent1.Equals(messageContent2).Should().BeTrue();
            (messageContent1 == messageContent2).Should().BeTrue();
        }

        [Fact]
        public void MessageContent_Equals_WithDifferentContent_ShouldNotBeEqual()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Goodbye World");

            // Act & Assert
            messageContent1.Should().NotBe(messageContent2);
            messageContent1.Equals(messageContent2).Should().BeFalse();
            (messageContent1 != messageContent2).Should().BeTrue();
        }

        [Fact]
        public void MessageContent_Equals_WithNull_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.Equals(null).Should().BeFalse();
        }

        [Fact]
        public void MessageContent_Equals_WithDifferentType_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");
            var otherObject = new object();

            // Act & Assert
            messageContent.Equals(otherObject).Should().BeFalse();
        }

        #endregion

        #region GetHashCode Tests

        [Fact]
        public void MessageContent_GetHashCode_WithSameContent_ShouldBeEqual()
        {
            // Arrange
            var content = "Hello World";
            var messageContent1 = new MessageContent(content);
            var messageContent2 = new MessageContent(content);

            // Act & Assert
            messageContent1.GetHashCode().Should().Be(messageContent2.GetHashCode());
        }

        [Fact]
        public void MessageContent_GetHashCode_WithDifferentContent_ShouldNotBeEqual()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Goodbye World");

            // Act & Assert
            messageContent1.GetHashCode().Should().NotBe(messageContent2.GetHashCode());
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void MessageContent_ToString_ShouldReturnValue()
        {
            // Arrange
            var content = "Hello World";
            var messageContent = new MessageContent(content);

            // Act
            var result = messageContent.ToString();

            // Assert
            result.Should().Be(content);
        }

        #endregion

        #region Operator Tests

        [Fact]
        public void MessageContent_EqualityOperator_WithSameValues_ShouldReturnTrue()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Hello World");

            // Act & Assert
            (messageContent1 == messageContent2).Should().BeTrue();
        }

        [Fact]
        public void MessageContent_EqualityOperator_WithDifferentValues_ShouldReturnFalse()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Goodbye World");

            // Act & Assert
            (messageContent1 == messageContent2).Should().BeFalse();
        }

        [Fact]
        public void MessageContent_InequalityOperator_WithSameValues_ShouldReturnFalse()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Hello World");

            // Act & Assert
            (messageContent1 != messageContent2).Should().BeFalse();
        }

        [Fact]
        public void MessageContent_InequalityOperator_WithDifferentValues_ShouldReturnTrue()
        {
            // Arrange
            var messageContent1 = new MessageContent("Hello World");
            var messageContent2 = new MessageContent("Goodbye World");

            // Act & Assert
            (messageContent1 != messageContent2).Should().BeTrue();
        }

        #endregion

        #region Property Tests

        [Fact]
        public void MessageContent_Empty_ShouldReturnEmptyMessageContent()
        {
            // Act
            var emptyContent = MessageContent.Empty;

            // Assert
            emptyContent.Value.Should().BeEmpty();
            emptyContent.Length.Should().Be(0);
            emptyContent.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void MessageContent_IsEmpty_WithEmptyContent_ShouldReturnTrue()
        {
            // Arrange
            var messageContent = new MessageContent("");

            // Act & Assert
            messageContent.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void MessageContent_IsEmpty_WithNonEmptyContent_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void MessageContent_Length_ShouldReturnCorrectLength()
        {
            // Arrange
            var content = "Hello World";
            var messageContent = new MessageContent(content);

            // Act & Assert
            messageContent.Length.Should().Be(content.Length);
        }

        #endregion

        #region Method Tests

        [Fact]
        public void MessageContent_Trim_ShouldReturnTrimmedContent()
        {
            // Arrange
            var messageContent = new MessageContent("  Hello World  ");

            // Act
            var trimmed = messageContent.Trim();

            // Assert
            trimmed.Value.Should().Be("Hello World");
        }

        [Fact]
        public void MessageContent_Substring_WithValidRange_ShouldReturnSubstring()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act
            var substring = messageContent.Substring(0, 5);

            // Assert
            substring.Value.Should().Be("Hello");
        }

        [Fact]
        public void MessageContent_Substring_WithInvalidRange_ShouldThrowArgumentException()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act
            var action = () => messageContent.Substring(0, 20);

            // Assert
            action.Should().Throw<ArgumentException>()
                .WithMessage("Start index and length must refer to a location within the string");
        }

        [Fact]
        public void MessageContent_Contains_WithExistingSubstring_ShouldReturnTrue()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.Contains("World").Should().BeTrue();
        }

        [Fact]
        public void MessageContent_Contains_WithNonExistingSubstring_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.Contains("Universe").Should().BeFalse();
        }

        [Fact]
        public void MessageContent_StartsWith_WithMatchingPrefix_ShouldReturnTrue()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.StartsWith("Hello").Should().BeTrue();
        }

        [Fact]
        public void MessageContent_StartsWith_WithNonMatchingPrefix_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.StartsWith("Goodbye").Should().BeFalse();
        }

        [Fact]
        public void MessageContent_EndsWith_WithMatchingSuffix_ShouldReturnTrue()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.EndsWith("World").Should().BeTrue();
        }

        [Fact]
        public void MessageContent_EndsWith_WithNonMatchingSuffix_ShouldReturnFalse()
        {
            // Arrange
            var messageContent = new MessageContent("Hello World");

            // Act & Assert
            messageContent.EndsWith("Universe").Should().BeFalse();
        }

        #endregion
    }
}