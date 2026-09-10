using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface ILLMFactory : IService {
        /// <summary>按 legacy 品牌/订阅枚举选择服务（Phase-3 调用方仍使用，必须保持可用）。</summary>
        ILLMService GetLLMService(LLMProvider provider);

        /// <summary>按 binding 线协议选择服务：OpenAIChat→OpenAIService，OpenAIResponses→OpenAIResponsesService，
        /// AnthropicMessages→AnthropicService，Ollama→OllamaService，Gemini→GeminiService。</summary>
        ILLMService GetLLMService(LlmProtocol protocol);

        /// <summary>按解析路由选择服务：有 binding 时按 binding.Protocol，否则（legacy 临时路由）按 channel.Provider。</summary>
        ILLMService GetLLMService(ResolvedLlmRoute route);
    }
}
