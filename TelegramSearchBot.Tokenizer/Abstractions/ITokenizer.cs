namespace TelegramSearchBot.Tokenizer.Abstractions;

/// <summary>
/// Represents a token with its character offsets in the original source text.
/// </summary>
/// <param name="Start">Inclusive start offset.</param>
/// <param name="End">Exclusive end offset following Lucene offset conventions.</param>
/// <param name="Term">The token text extracted from the source.</param>
public record TokenWithOffset(int Start, int End, string Term);

/// <summary>
/// Provides tokenization services for search-related text processing.
/// </summary>
public interface ITokenizer {
    /// <summary>
    /// Tokenizes text into normalized search terms.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>
    /// A de-duplicated list of tokens suitable for keyword-style search.
    /// Implementations may discard ordering and duplicate terms.
    /// </returns>
    IReadOnlyList<string> Tokenize(string text);

    /// <summary>
    /// Tokenizes text using a best-effort fallback strategy for user-facing search flows.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>
    /// A list of tokens suitable for search, preferring graceful fallback behavior instead of tokenizer-specific failures.
    /// </returns>
    IReadOnlyList<string> SafeTokenize(string text);

    /// <summary>
    /// Tokenizes text while preserving token order and character offsets.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>
    /// Ordered tokens whose offsets align with the original string indices. Use this for phrase and snippet operations.
    /// </returns>
    IReadOnlyList<TokenWithOffset> TokenizeWithOffsets(string text);

    /// <summary>
    /// Describes the tokenizer implementation and its capabilities.
    /// </summary>
    TokenizerMetadata Metadata { get; }
}
