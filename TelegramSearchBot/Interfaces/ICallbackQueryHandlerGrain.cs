using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for handling callback queries from Telegram inline keyboards.
    /// </summary>
    public interface ICallbackQueryHandlerGrain : IGrainWithStringKey // Callback queries often identified by string data
    {
        // This grain consumes RawCallbackQueryMessages.
        // A method to explicitly handle a callback query:
        // Task HandleCallbackQueryAsync(string callbackData, long chatId, int messageId, long userId);
        // The key for this grain could be the callback query prefix or a unique identifier.
    }
}
