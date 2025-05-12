using Orleans;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups; // Added for IReplyMarkup

namespace TelegramSearchBot.Interfaces
{
    // Define a simple message model for sending, can be expanded with more properties like ParseMode, ReplyMarkup etc.
    public class TelegramMessageToSend
    {
        public long ChatId { get; set; }
        public string Text { get; set; }
        public int? ReplyToMessageId { get; set; }
        public IReplyMarkup ReplyMarkup { get; set; } // Added for Inline Keyboards etc.
        // Add other properties like ParseMode etc. as needed
    }

    /// <summary>
    /// Grain interface responsible for sending messages via the Telegram Bot API.
    /// This acts as a centralized sender for other grains.
    /// </summary>
    public interface ITelegramMessageSenderGrain : IGrainWithIntegerKey // Could be a stateless worker, or keyed if needed (e.g. by BotId if multi-bot)
    {
        Task SendMessageAsync(TelegramMessageToSend message);
        // Add other methods like SendPhotoAsync, EditMessageTextAsync etc. as they become needed.
    }
}
