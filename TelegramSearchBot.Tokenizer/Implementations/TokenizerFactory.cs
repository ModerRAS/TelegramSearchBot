using System;
using System.Collections.Concurrent;
using TelegramSearchBot.Tokenizer.Abstractions;

namespace TelegramSearchBot.Tokenizer.Implementations;

public class TokenizerFactory : ITokenizerFactory {
    private readonly Action<string>? _logAction;
    private readonly ConcurrentDictionary<TokenizerType, ITokenizer> _tokenizers = new();

    public TokenizerFactory(Action<string>? logAction = null) {
        _logAction = logAction;
    }

    public ITokenizer Create(TokenizerType type) {
        return _tokenizers.GetOrAdd(type, CreateTokenizer);
    }

    private ITokenizer CreateTokenizer(TokenizerType type) {
        return type switch {
            TokenizerType.SmartChinese => new SmartChineseTokenizer(_logAction),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown tokenizer type: {type}")
        };
    }
}
