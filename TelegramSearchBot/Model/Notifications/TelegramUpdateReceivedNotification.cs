using MediatR;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model.Notifications
{
    /// <summary>
    /// Notification published when any update is received from Telegram.
    /// Handlers will subscribe to this and filter based on the Update content.
    /// </summary>
    public class TelegramUpdateReceivedNotification : INotification
    {
        public Update Update { get; }

        public TelegramUpdateReceivedNotification(Update update)
        {
            Update = update;
        }
    }
}
