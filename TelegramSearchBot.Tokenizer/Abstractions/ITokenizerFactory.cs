namespace TelegramSearchBot.Tokenizer.Abstractions;

public interface ITokenizerFactory
{
    ITokenizer Create(TokenizerType type);
}
