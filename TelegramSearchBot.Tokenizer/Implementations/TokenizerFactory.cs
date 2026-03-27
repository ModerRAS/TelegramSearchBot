using TelegramSearchBot.Tokenizer.Abstractions;

namespace TelegramSearchBot.Tokenizer.Implementations;

public class TokenizerFactory : ITokenizerFactory
{
    public ITokenizer Create(TokenizerType type)
    {
        return type switch
        {
            TokenizerType.SmartChinese => new SmartChineseTokenizer(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown tokenizer type: {type}")
        };
    }
}
