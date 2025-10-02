using TelegramSearchBot.Core.Model.AI;

namespace TelegramSearchBot.Core.Interface.AI.LLM {
    public interface ILLMFactory : IService {
        ILLMService GetLLMService(LLMProvider provider);
    }
}
