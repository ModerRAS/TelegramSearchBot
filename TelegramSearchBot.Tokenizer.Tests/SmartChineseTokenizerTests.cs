using TelegramSearchBot.Tokenizer.Implementations;
using Xunit;

namespace TelegramSearchBot.Tokenizer.Tests;

public class SmartChineseTokenizerTests {
    private readonly SmartChineseTokenizer _tokenizer;

    public SmartChineseTokenizerTests() {
        _tokenizer = new SmartChineseTokenizer();
    }

    [Fact]
    public void Metadata_ReturnsSmartChineseInfo() {
        // Assert
        Assert.Equal("SmartChinese", _tokenizer.Metadata.Name);
        Assert.Equal("Chinese", _tokenizer.Metadata.Language);
        Assert.True(_tokenizer.Metadata.SupportsNLP);
    }

    [Fact]
    public void Tokenize_ReturnsTokens_ForChineseText() {
        // Act
        var result = _tokenizer.Tokenize("今天天气真好");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Tokenize_ReturnsTokens_ForEnglishText() {
        // Act
        var result = _tokenizer.Tokenize("Hello World");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Tokenize_ReturnsTokens_ForMixedText() {
        // Act
        var result = _tokenizer.Tokenize("今天天气很好，Hello World");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Tokenize_ReturnsDistinctTokens() {
        // Act
        var result = _tokenizer.Tokenize("测试 测试 测试");

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public void SafeTokenize_ReturnsFallback_ForEmptyText() {
        // Act
        var result = _tokenizer.SafeTokenize("");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void SafeTokenize_ReturnsTokens_ForNormalText() {
        // Act
        var result = _tokenizer.SafeTokenize("这是一个测试");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void SafeTokenize_ReturnsTokens_ForWhitespaceSeparatedText() {
        // Act
        var result = _tokenizer.SafeTokenize("hello world");

        // Assert
        Assert.Contains("hello", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void Tokenize_WithLogAction_DoesNotThrow() {
        // Arrange
        var logger = new List<string>();
        var tokenizerWithLog = new SmartChineseTokenizer(msg => logger.Add(msg));

        // Act
        var result = tokenizerWithLog.Tokenize("测试文本");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void SafeTokenize_WithLogAction_DoesNotThrow() {
        // Arrange
        var logger = new List<string>();
        var tokenizerWithLog = new SmartChineseTokenizer(msg => logger.Add(msg));

        // Act
        var result = tokenizerWithLog.SafeTokenize("测试文本");

        // Assert
        Assert.NotNull(result);
    }
}
