namespace TelegramSearchBot.AIApi.Model.ChatModel
{
    public class ChatRequest
    {
        public string model { get; set; }
        public List<Message> messages { get; set; }
        public double temperature { get; set; } = 1.0;
        public int max_tokens { get; set; } = 512;
        public bool stream { get; set; } = false;
    }
}
