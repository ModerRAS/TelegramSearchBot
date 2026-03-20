namespace TelegramSearchBot.Model.Tools {
    public class SendVideoResult {
        public bool Success { get; set; }
        public int? MessageId { get; set; }
        public long ChatId { get; set; }
        public string Error { get; set; }
    }
}
