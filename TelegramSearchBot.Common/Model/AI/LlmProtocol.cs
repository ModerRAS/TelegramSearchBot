namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// API binding 的线协议。与 LLMProvider（品牌/订阅账号）语义分离，
    /// 同一 channel 可通过多个 binding 支持不同协议。
    /// </summary>
    public enum LlmProtocol {
        OpenAIChat = 0,
        OpenAIResponses = 1,
        AnthropicMessages = 2,
        Ollama = 3,
        Gemini = 4
    }
}
