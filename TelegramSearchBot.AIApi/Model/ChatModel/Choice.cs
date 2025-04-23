namespace TelegramSearchBot.AIApi.Model.ChatModel
{
    public class Choice
    {
        public int index { get; set; } = 0;
        public Message message { get; set; }
        public string finish_reason { get; set; } = "stop";
    }
}
