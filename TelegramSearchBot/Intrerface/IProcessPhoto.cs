using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;

namespace TelegramSearchBot.Intrerface {
    public interface IProcessPhoto {
        public static bool IsPhoto(string Name) {
            if (string.IsNullOrEmpty(Name)) {
                return false;
            } else if (
                Name.ToLower().EndsWith(".jpg") ||
                Name.ToLower().EndsWith(".jpeg") ||
                Name.ToLower().EndsWith(".png") ||
                Name.ToLower().EndsWith(".webp") ||
                Name.ToLower().EndsWith(".heic")) {
                return true;
            } else {
                return false;
            }
        }
        public static async Task<Stream> GetPhoto(ITelegramBotClient botClient, Update e) {
            string FileId = string.Empty;
            if (e?.Message?.Photo?.Length is not null && e?.Message?.Photo?.Length > 0) {
                FileId = e.Message.Photo.Last().FileId;
            } else if (e?.Message?.Document is not null && IsPhoto(e?.Message?.Document.FileName)) {
                FileId = e?.Message?.Document.FileId;
            } else {
                throw new CannotGetPhotoException();
            }

            if (Env.IsLocalAPI) {
                var fileInfo = await botClient.GetFileAsync(FileId);
                var client = new HttpClient();
                using (var stream = await client.GetStreamAsync($"{Env.BaseUrl}{fileInfo.FilePath}")) {
                    return stream;
                }
            } else {
                using (var stream = new MemoryStream()) {
                    var file = await botClient.GetInfoAndDownloadFileAsync(FileId, stream);
                    stream.Position = 0;
                    return stream;
                }
            }
        }
    }
}
