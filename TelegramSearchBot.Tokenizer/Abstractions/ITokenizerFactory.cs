namespace TelegramSearchBot.Tokenizer.Abstractions;

/// <summary>
/// Creates tokenizer implementations by type.
/// </summary>
public interface ITokenizerFactory {
    ITokenizer Create(TokenizerType type);
}
