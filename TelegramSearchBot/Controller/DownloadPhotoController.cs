using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using File = System.IO.File;

namespace TelegramSearchBot.Controller {
    public class DownloadPhotoController : IPreUpdate {
        public ITelegramBotClient botClient { get; private set; }
        public string PhotoDirectory { get; private set; } = Path.Combine(Env.WorkDir, "Photos");
        public DownloadPhotoController(ITelegramBotClient botClient) { 
            this.botClient = botClient;
            if (!Directory.Exists(PhotoDirectory)) {
                Directory.CreateDirectory(PhotoDirectory);
            }
        }

        public async Task ExecuteAsync(Update e) {
            var (PhotoName, PhotoByte) = await IProcessPhoto.DownloadPhoto(botClient, e);
            var chatid = e.Message.Chat.Id;
            var FilePath = Path.Combine(PhotoDirectory, $"{chatid}");
            if (!Directory.Exists(FilePath)) {
                Directory.CreateDirectory(FilePath);
            }
            await File.WriteAllBytesAsync(Path.Combine(FilePath, PhotoName), PhotoByte);
        }
    }
}
