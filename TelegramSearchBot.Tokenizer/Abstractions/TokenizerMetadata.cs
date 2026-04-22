namespace TelegramSearchBot.Tokenizer.Abstractions;

/// <summary>
/// Describes a tokenizer implementation and its general capabilities.
/// </summary>
public record TokenizerMetadata(
    string Name,
    string Language,
    bool SupportsNLP
);
