namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// API binding 的认证方式。key 本体仍共享自 LLMChannel.ApiKey。
    /// </summary>
    public enum LlmAuthProfile {
        Bearer = 0,
        AnthropicApiKey = 1,
        None = 2
    }
}
