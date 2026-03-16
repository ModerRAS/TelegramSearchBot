namespace TelegramSearchBot.Model.Tools {
    public class SendPhotoResult {
        public bool Success { get; set; }
        public int? MessageId { get; set; }
        public long ChatId { get; set; }
        public string Error { get; set; }
    }
}
