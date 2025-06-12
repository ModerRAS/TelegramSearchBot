using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface ILLMFactory : IService {
        ILLMService GetLLMService(LLMProvider provider);
    }
}
