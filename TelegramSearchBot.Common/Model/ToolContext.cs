namespace TelegramSearchBot.Model {
    public class ToolContext {
        public long ChatId { get; set; }
        public long UserId { get; set; }
        /// <summary>
        /// The original user message ID, used as default reply target for tool actions (e.g. sending photos).
        /// </summary>
        public int MessageId { get; set; }
    }
}
