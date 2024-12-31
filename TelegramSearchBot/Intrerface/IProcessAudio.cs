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

namespace TelegramSearchBot.Intrerface {
    public interface IProcessAudio {
        public static bool IsAudio(string Name) {
            if (string.IsNullOrEmpty(Name)) {
                return false;
            } else if (
                Name.ToLower().EndsWith(".mp3") ||
                Name.ToLower().EndsWith(".wav") ||
                Name.ToLower().EndsWith(".ogg") ||
                Name.ToLower().EndsWith(".flac") ||
                Name.ToLower().EndsWith(".alac") ||
                Name.ToLower().EndsWith(".ape") ||
                Name.ToLower().EndsWith(".aac") ||
                Name.ToLower().EndsWith(".opus") ||
                Name.ToLower().EndsWith(".mka") ||
                Name.ToLower().EndsWith(".wma")) {
                return true;
            } else {
                return false;
            }
        }

        public async static Task<byte[]> ConvertToWav(byte[] source) {
            using var inputStream = new MemoryStream();
            using var outputStream = new MemoryStream();
            inputStream.Write(source, 0, source.Length);
            inputStream.Position = 0;
            await FFMpegArguments
                .FromPipeInput(new StreamPipeSource(inputStream))
                .OutputToPipe(new StreamPipeSink(outputStream), options => options
                    .DisableChannel(FFMpegCore.Enums.Channel.Video)
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 2 -f wav")
                    .WithFastStart())
                .ProcessAsynchronously();

            outputStream.Position = 0;

            // Write the image to the memorystream
            return outputStream.ToArray();
        }
        public static string GetAudioPath(Update e) {
            try {
                var DirPath = Path.Combine(Env.WorkDir, "Audios", $"{e.Message.Chat.Id}");
                var files = Directory.GetFiles(DirPath, $"{e.Message.MessageId}.*");
                if (files.Length == 0) {
                    throw new CannotGetAudioException();
                }
                return files.FirstOrDefault();
            } catch(NullReferenceException) {
                //Console.WriteLine(err.Message);
                throw new CannotGetAudioException();
            }
        }
        public static async Task<byte[]> GetAudio(Update e) {
            var FilePath = GetAudioPath(e);
            return await File.ReadAllBytesAsync(FilePath);

        }
        public static async Task<(string, byte[])> DownloadAudio(ITelegramBotClient botClient, Update e) {
            string FileId = string.Empty;
            string FileName = string.Empty;
            if (e?.Message?.Audio is not null) {
                FileId = e.Message.Audio.FileId;
                FileName = $"{e.Message.MessageId}{Path.GetExtension(e.Message.Audio.FileName)}";
            } else if (e?.Message?.Voice is not null) {
                FileId = e.Message.Voice.FileId;
                FileName = $"{e.Message.MessageId}.ogg";
            } else if (e?.Message?.Document is not null && IsAudio(e?.Message?.Document.FileName)) {
                FileId = e?.Message?.Document.FileId;

                FileName = $"{e.Message.MessageId}{Path.GetExtension(e?.Message?.Document.FileName)}";
            } else {
                throw new CannotGetAudioException();
            }
            using (var stream = new MemoryStream()) {
                if (Env.IsLocalAPI) {
                    var fileInfo = await botClient.GetFileAsync(FileId);
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
