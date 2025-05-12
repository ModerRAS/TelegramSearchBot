using Orleans;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums; // Added for ParseMode
using Telegram.Bot.Types.ReplyMarkups; 

namespace TelegramSearchBot.Interfaces
{
    // Define a simple message model for sending
    public class TelegramMessageToSend
    {
        public long ChatId { get; set; }
        public string Text { get; set; }
        public int? ReplyToMessageId { get; set; }
        public IReplyMarkup ReplyMarkup { get; set; }
        public ParseMode? ParseMode { get; set; } // Added for specifying message parsing mode
        public bool? DisableWebPagePreview { get; set; } // Added to control web page preview
    }

    /// <summary>
    /// Grain interface responsible for sending messages via the Telegram Bot API.
    /// This acts as a centralized sender for other grains.
    /// </summary>
    public interface ITelegramMessageSenderGrain : IGrainWithIntegerKey // Could be a stateless worker, or keyed if needed (e.g. by BotId if multi-bot)
    {
        /// <summary>
        /// Sends a message and returns the MessageId of the sent message if successful.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>The MessageId of the sent message, or null if sending failed or ID is not available.</returns>
        Task<int?> SendMessageAsync(TelegramMessageToSend message);
        
        /// <summary>
        /// Deletes a message.
        /// </summary>
        /// <param name="chatId">Chat ID.</param>
        /// <param name="messageId">Message ID to delete.</param>
        Task DeleteMessageAsync(long chatId, int messageId);
        // Add other methods like SendPhotoAsync, EditMessageTextAsync etc. as they become needed.
    }
}
