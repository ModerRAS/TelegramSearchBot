namespace TelegramSearchBot.AIApi.Model.BackendServer
{
    public class BackendServerSettings
    {
        public string Name { get; set; } // 服务器名称，用于标识
        public string Url { get; set; } // 服务器的 base URL (例如: "https://api.openai.com", "http://localhost:11434")
        public BackendType Type { get; set; } // 服务器类型 (OpenAI 或 Ollama)
        public string? ApiKey { get; set; } // 如果是 OpenAI，可能需要 API Key
    }
}
