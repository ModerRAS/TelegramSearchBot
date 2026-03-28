using TelegramSearchBot.Tokenizer.Abstractions;
using TelegramSearchBot.Tokenizer.Implementations;
using Xunit;

namespace TelegramSearchBot.Tokenizer.Tests;

public class TokenizerFactoryTests {
    private readonly TokenizerFactory _factory;

    public TokenizerFactoryTests() {
        _factory = new TokenizerFactory();
    }

    [Fact]
    public void Create_SmartChinese_ReturnsSmartChineseTokenizer() {
        // Act
        var tokenizer = _factory.Create(TokenizerType.SmartChinese);

        // Assert
        Assert.NotNull(tokenizer);
        Assert.IsType<SmartChineseTokenizer>(tokenizer);
    }

    [Fact]
    public void Create_SmartChinese_ReturnsTokenizerWithCorrectMetadata() {
        // Act
        var tokenizer = _factory.Create(TokenizerType.SmartChinese);

        // Assert
        Assert.Equal("SmartChinese", tokenizer.Metadata.Name);
        Assert.Equal("Chinese", tokenizer.Metadata.Language);
    }

    [Fact]
    public void Create_UnknownType_ThrowsArgumentOutOfRangeException() {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _factory.Create(( TokenizerType ) 999));

        Assert.Contains("Unknown tokenizer type", exception.Message);
    }

    [Fact]
    public void Create_ReturnsTokenizerThatImplementsITokenizer() {
        // Act
        var tokenizer = _factory.Create(TokenizerType.SmartChinese);

        // Assert
        Assert.IsAssignableFrom<ITokenizer>(tokenizer);
    }
}
