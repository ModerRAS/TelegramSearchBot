using MediatR;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Model.Notifications
{
    /// <summary>
    /// Notification published after an audio file has been successfully downloaded.
    /// </summary>
    public class AudioDownloadedNotification : INotification
    {
        public string FilePath { get; }
        public Update OriginalUpdate { get; } // To provide context like ChatId, MessageId for subsequent handlers

        public AudioDownloadedNotification(string filePath, Update originalUpdate)
        {
            FilePath = filePath;
            OriginalUpdate = originalUpdate;
        }
    }
}
