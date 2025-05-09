using MediatR;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model.Notifications
{
    /// <summary>
    /// Notification published after a photo has been successfully downloaded.
    /// </summary>
    public class PhotoDownloadedNotification : INotification
    {
        public string FilePath { get; }
        public Update OriginalUpdate { get; } // To provide context like ChatId, MessageId for subsequent handlers

        public PhotoDownloadedNotification(string filePath, Update originalUpdate)
        {
            FilePath = filePath;
            OriginalUpdate = originalUpdate;
        }
    }
}
