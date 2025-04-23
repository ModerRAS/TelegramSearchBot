namespace TelegramSearchBot.AIApi.Model.ChatModel
{
    public class Usage
    {
        public int prompt_tokens { get; set; } = 20;
        public int completion_tokens { get; set; } = 10;
        public int total_tokens => prompt_tokens + completion_tokens;
    }
}
