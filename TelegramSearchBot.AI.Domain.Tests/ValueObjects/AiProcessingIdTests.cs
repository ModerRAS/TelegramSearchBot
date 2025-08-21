using System;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Tests.ValueObjects
{
    public class AiProcessingIdTests
    {
        [Fact]
        public void Create_ShouldReturnNewId()
        {
            // Act
            var id = AiProcessingId.Create();

            // Assert
            id.Should().NotBeNull();
            id.Value.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public void From_ShouldReturnIdWithGivenValue()
        {
            // Arrange
            var guid = Guid.NewGuid();

            // Act
            var id = AiProcessingId.From(guid);

            // Assert
            id.Value.Should().Be(guid);
        }

        [Fact]
        public void Constructor_WithEmptyGuid_ShouldThrowArgumentException()
        {
            // Act & Assert
            var action = () => new AiProcessingId(Guid.Empty);
            action.Should().Throw<ArgumentException>()
                .WithMessage("AI processing ID cannot be empty*");
        }

        [Fact]
        public void Equals_WithSameValue_ShouldReturnTrue()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var id1 = AiProcessingId.From(guid);
            var id2 = AiProcessingId.From(guid);

            // Act & Assert
            id1.Should().Be(id2);
            (id1 == id2).Should().BeTrue();
        }

        [Fact]
        public void Equals_WithDifferentValue_ShouldReturnFalse()
        {
            // Arrange
            var id1 = AiProcessingId.From(Guid.NewGuid());
            var id2 = AiProcessingId.From(Guid.NewGuid());

            // Act & Assert
            id1.Should().NotBe(id2);
            (id1 != id2).Should().BeTrue();
        }

        [Fact]
        public void GetHashCode_WithSameValue_ShouldReturnSameHashCode()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var id1 = AiProcessingId.From(guid);
            var id2 = AiProcessingId.From(guid);

            // Act & Assert
            id1.GetHashCode().Should().Be(id2.GetHashCode());
        }

        [Fact]
        public void ToString_ShouldReturnGuidString()
        {
            // Arrange
            var guid = Guid.NewGuid();
            var id = AiProcessingId.From(guid);

            // Act & Assert
            id.ToString().Should().Be(guid.ToString());
        }
    }
}