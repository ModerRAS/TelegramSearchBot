using ImageMagick;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using File = System.IO.File;

namespace TelegramSearchBot.Interface {
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
        public static byte[] ConvertToJpeg(byte[] source) {
            // Read first frame of gif image
            using var image = new MagickImage(source);

            using var memStream = new MemoryStream();

            // Sets the output format to png
            image.Format = MagickFormat.Jpeg;

            // Write the image to the memorystream
            return image.ToByteArray();
        }
        public static string GetPhotoPath(Update e) {
            try {
                var DirPath = Path.Combine(Env.WorkDir, "Photos", $"{e.Message.Chat.Id}");
                var files = Directory.GetFiles(DirPath, $"{e.Message.MessageId}.*");
                if (files.Length == 0) {
                    throw new CannotGetPhotoException();
                }
                return files.FirstOrDefault();
            } catch(NullReferenceException) {
                //Console.WriteLine(err.Message);
                throw new CannotGetPhotoException();
            }
        }
        public static async Task<byte[]> GetPhoto(Update e) {
            var FilePath = GetPhotoPath(e);
            var file = await File.ReadAllBytesAsync(FilePath);
            return ConvertToJpeg(file);

        }
        public static async Task<(string, byte[])> DownloadPhoto(ITelegramBotClient botClient, Update e) {
            string FileId = string.Empty;
            string FileName = string.Empty;
            if (e?.Message?.Photo?.Length is not null && e?.Message?.Photo?.Length > 0) {
                FileId = e.Message.Photo.Last().FileId;
                FileName = $"{e.Message.MessageId}.jpg";
            } else if (e?.Message?.Document is not null && IsPhoto(e?.Message?.Document.FileName)) {
                FileId = e?.Message?.Document.FileId;
                
                FileName = $"{e.Message.MessageId}{Path.GetExtension(e?.Message?.Document.FileName)}";
            } else {
                throw new CannotGetPhotoException();
            }
            using (var stream = new MemoryStream()) {
                if (Env.IsLocalAPI) {
                    var fileInfo = await botClient.GetFile(FileId);
                    var client = new HttpClient();
                    if (Env.SameServer) {
                        using (var filestream = new FileStream(fileInfo.FilePath, FileMode.Open, FileAccess.Read)) {
                            await filestream.CopyToAsync(stream);
                        }
                    } else {
                        using (var clistream = await client.GetStreamAsync($"{Env.BaseUrl}{fileInfo.FilePath}")) {
                            await clistream.CopyToAsync(stream);
                        }
                    }
                } else {
                    var fileInfo = await botClient.GetFile(FileId);
                    var filePath = fileInfo.FilePath;
                    await botClient.DownloadFile(filePath, stream);
                    stream.Position = 0;
                }

                return (FileName, stream.ToArray());
            }

        }
    }
}
