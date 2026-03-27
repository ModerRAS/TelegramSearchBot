namespace TelegramSearchBot.Tokenizer.Abstractions;

public record TokenWithOffset(int Start, int End, string Term);

public interface ITokenizer
{
    IReadOnlyList<string> Tokenize(string text);
    IReadOnlyList<string> SafeTokenize(string text);
    IReadOnlyList<TokenWithOffset> TokenizeWithOffsets(string text);
    TokenizerMetadata Metadata { get; }
}
