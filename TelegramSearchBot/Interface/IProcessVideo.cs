using FFMpegCore.Pipes;
using FFMpegCore;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using File = System.IO.File;

namespace TelegramSearchBot.Interface {
    public interface IProcessVideo {
        public static bool IsVideo(string Name) {
            if (string.IsNullOrEmpty(Name)) {
                return false;
            } else if (
                Name.ToLower().EndsWith(".mp4") ||
                Name.ToLower().EndsWith(".mov") ||
                Name.ToLower().EndsWith(".mkv") ||
                Name.ToLower().EndsWith(".flv") ||
                Name.ToLower().EndsWith(".m4v") ||
                Name.ToLower().EndsWith(".avi") ||
                Name.ToLower().EndsWith(".webm") ||
                Name.ToLower().EndsWith(".opus") ||
                Name.ToLower().EndsWith(".3gp") ||
                Name.ToLower().EndsWith(".mpg")) {
                return true;
            } else {
                return false;
            }
        }
        public static string GetVideoPath(Update e) {
            try {
                var DirPath = Path.Combine(Env.WorkDir, "Videos", $"{e.Message.Chat.Id}");
                var files = Directory.GetFiles(DirPath, $"{e.Message.MessageId}.*");
                if (files.Length == 0) {
                    throw new CannotGetVideoException();
                }
                return files.FirstOrDefault();
            } catch(NullReferenceException) {
                //Console.WriteLine(err.Message);
                throw new CannotGetVideoException();
            }
        }
        public static async Task<byte[]> GetVideo(Update e) {
            var FilePath = GetVideoPath(e);
            return await File.ReadAllBytesAsync(FilePath);

        }
        public static async Task<(string, byte[])> DownloadVideo(ITelegramBotClient botClient, Update e) {
            string FileId = string.Empty;
            string FileName = string.Empty;
            if (e?.Message?.Video is not null) {
                FileId = e.Message.Video.FileId;
                FileName = $"{e.Message.MessageId}.mp4";
            } else if (e?.Message?.Document is not null && IsVideo(e?.Message?.Document.FileName)) {
                FileId = e?.Message?.Document.FileId;
                FileName = $"{e.Message.MessageId}{Path.GetExtension(e?.Message?.Document.FileName)}";
            } else {
                throw new CannotGetVideoException();
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
