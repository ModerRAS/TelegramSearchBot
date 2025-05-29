using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface ILLMFactory : IService {
        ILLMService GetLLMService(LLMProvider provider);
    }
}
