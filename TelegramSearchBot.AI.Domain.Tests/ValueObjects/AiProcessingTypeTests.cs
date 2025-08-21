using System;
using Xunit;
using FluentAssertions;
using TelegramSearchBot.AI.Domain.ValueObjects;

namespace TelegramSearchBot.AI.Domain.Tests.ValueObjects
{
    public class AiProcessingTypeTests
    {
        [Fact]
        public void OCR_ShouldReturnCorrectType()
        {
            // Act
            var type = AiProcessingType.OCR;

            // Assert
            type.Value.Should().Be("OCR");
        }

        [Fact]
        public void ASR_ShouldReturnCorrectType()
        {
            // Act
            var type = AiProcessingType.ASR;

            // Assert
            type.Value.Should().Be("ASR");
        }

        [Fact]
        public void LLM_ShouldReturnCorrectType()
        {
            // Act
            var type = AiProcessingType.LLM;

            // Assert
            type.Value.Should().Be("LLM");
        }

        [Fact]
        public void Vector_ShouldReturnCorrectType()
        {
            // Act
            var type = AiProcessingType.Vector;

            // Assert
            type.Value.Should().Be("Vector");
        }

        [Fact]
        public void MultiModal_ShouldReturnCorrectType()
        {
            // Act
            var type = AiProcessingType.MultiModal;

            // Assert
            type.Value.Should().Be("MultiModal");
        }

        [Fact]
        public void From_WithKnownType_ShouldReturnCorrectInstance()
        {
            // Act & Assert
            AiProcessingType.From("OCR").Should().Be(AiProcessingType.OCR);
            AiProcessingType.From("ASR").Should().Be(AiProcessingType.ASR);
            AiProcessingType.From("LLM").Should().Be(AiProcessingType.LLM);
            AiProcessingType.From("Vector").Should().Be(AiProcessingType.Vector);
            AiProcessingType.From("MultiModal").Should().Be(AiProcessingType.MultiModal);
        }

        [Fact]
        public void From_WithUnknownType_ShouldReturnNewInstance()
        {
            // Arrange
            var unknownType = "UnknownType";

            // Act
            var type = AiProcessingType.From(unknownType);

            // Assert
            type.Value.Should().Be(unknownType);
            type.Should().NotBe(AiProcessingType.OCR);
            type.Should().NotBe(AiProcessingType.ASR);
            type.Should().NotBe(AiProcessingType.LLM);
            type.Should().NotBe(AiProcessingType.Vector);
            type.Should().NotBe(AiProcessingType.MultiModal);
        }

        [Fact]
        public void From_WithNullOrEmpty_ShouldThrowArgumentException()
        {
            // Act & Assert
            var action = () => AiProcessingType.From(null);
            action.Should().Throw<ArgumentException>()
                .WithMessage("AI processing type cannot be null or empty*");

            action = () => AiProcessingType.From("");
            action.Should().Throw<ArgumentException>()
                .WithMessage("AI processing type cannot be null or empty*");

            action = () => AiProcessingType.From("   ");
            action.Should().Throw<ArgumentException>()
                .WithMessage("AI processing type cannot be null or empty*");
        }

        [Fact]
        public void Equals_WithSameValue_ShouldReturnTrue()
        {
            // Arrange
            var type1 = AiProcessingType.OCR;
            var type2 = AiProcessingType.From("OCR");

            // Act & Assert
            type1.Should().Be(type2);
            (type1 == type2).Should().BeTrue();
        }

        [Fact]
        public void Equals_WithDifferentValue_ShouldReturnFalse()
        {
            // Arrange
            var type1 = AiProcessingType.OCR;
            var type2 = AiProcessingType.ASR;

            // Act & Assert
            type1.Should().NotBe(type2);
            (type1 != type2).Should().BeTrue();
        }

        [Fact]
        public void Equals_WithDifferentCase_ShouldReturnTrue()
        {
            // Arrange
            var type1 = AiProcessingType.OCR;
            var type2 = AiProcessingType.From("ocr");

            // Act & Assert
            type1.Should().Be(type2);
            (type1 == type2).Should().BeTrue();
        }

        [Fact]
        public void GetHashCode_WithSameValue_ShouldReturnSameHashCode()
        {
            // Arrange
            var type1 = AiProcessingType.OCR;
            var type2 = AiProcessingType.From("OCR");

            // Act & Assert
            type1.GetHashCode().Should().Be(type2.GetHashCode());
        }
    }
}