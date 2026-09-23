namespace TelegramSearchBot.Model.AI {
    public enum LLMProvider {
        None,
        OpenAI,
        Ollama,
        Gemini,
        MiniMax = 4,
        LMStudio = 5,
        Anthropic = 6,
        ResponsesAPI = 7,
        Mistral = 8,
        AzureOpenAI = 9,
        Vertex = 10,
        Bedrock = 11,
        Radius = 12
    }
}
