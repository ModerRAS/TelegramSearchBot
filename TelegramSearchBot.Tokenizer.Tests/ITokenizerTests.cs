using TelegramSearchBot.Tokenizer.Abstractions;
using Xunit;

namespace TelegramSearchBot.Tokenizer.Tests;

public class ITokenizerTests {
    [Fact]
    public void Tokenize_ReturnsNonEmptyList_ForValidText() {
        // Arrange
        var tokenizer = new MockTokenizer();

        // Act
        var result = tokenizer.Tokenize("这是一个测试");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Tokenize_ReturnsDistinctTokens() {
        // Arrange
        var tokenizer = new MockTokenizer();

        // Act
        var result = tokenizer.Tokenize("测试 测试 测试");

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public void SafeTokenize_ReturnsNonEmptyList_ForValidText() {
        // Arrange
        var tokenizer = new MockTokenizer();

        // Act
        var result = tokenizer.SafeTokenize("这是一个测试");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Metadata_IsNotNull() {
        // Arrange
        var tokenizer = new MockTokenizer();

        // Act & Assert
        Assert.NotNull(tokenizer.Metadata);
    }

    private class MockTokenizer : ITokenizer {
        public TokenizerMetadata Metadata { get; } = new TokenizerMetadata("Mock", "Test", false);

        public IReadOnlyList<string> Tokenize(string text) {
            return text.Split(' ').Distinct().ToList();
        }

        public IReadOnlyList<string> SafeTokenize(string text) {
            return Tokenize(text);
        }

        public IReadOnlyList<TokenWithOffset> TokenizeWithOffsets(string text) {
            var tokens = new List<TokenWithOffset>();
            var words = text.Split(' ');
            int pos = 0;
            foreach (var word in words) {
                if (!string.IsNullOrEmpty(word)) {
                    tokens.Add(new TokenWithOffset(pos, pos + word.Length, word));
                }
                pos += word.Length + 1;
            }
            return tokens;
        }
    }
}
