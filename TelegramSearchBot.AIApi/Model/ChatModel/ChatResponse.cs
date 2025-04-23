namespace TelegramSearchBot.AIApi.Model.ChatModel
{
    public class ChatResponse
    {
        public string id { get; set; } = "chatcmpl-mock123";
        public string @object { get; set; } = "chat.completion";
        public long created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public string model { get; set; }
        public List<Choice> choices { get; set; }
        public Usage usage { get; set; }
    }
}
