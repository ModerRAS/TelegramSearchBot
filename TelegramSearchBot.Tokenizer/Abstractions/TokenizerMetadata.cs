namespace TelegramSearchBot.Tokenizer.Abstractions;

public record TokenizerMetadata(
    string Name,
    string Language,
    bool SupportsNLP
);
