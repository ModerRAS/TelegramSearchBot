using FFMpegCore;
using FFMpegCore.Pipes;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interfaces; // For IAudioProcessingService
using File = System.IO.File;

// Assuming Env.cs is in this namespace or accessible.
// using TelegramSearchBot; 

namespace TelegramSearchBot.Service.Media
{
    public class AudioProcessingService : IAudioProcessingService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly HttpClient _httpClient; // For DownloadAudioAsync if Env.IsLocalAPI and !Env.SameServer

        // TODO: Consider injecting IConfiguration or IOptions<MySettings> to replace static Env access.
        public AudioProcessingService(ITelegramBotClient botClient)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _httpClient = new HttpClient(); // Initialize HttpClient here or inject IHttpClientFactory
        }

        public bool IsAudio(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }
            // Simplified for brevity, original logic from IProcessAudio.IsAudio
            string lowerFileName = fileName.ToLower();
            return lowerFileName.EndsWith(".mp3") ||
                   lowerFileName.EndsWith(".wav") ||
                   lowerFileName.EndsWith(".ogg") ||
                   lowerFileName.EndsWith(".flac") ||
                   lowerFileName.EndsWith(".alac") ||
                   lowerFileName.EndsWith(".ape") ||
                   lowerFileName.EndsWith(".aac") ||
                   lowerFileName.EndsWith(".opus") ||
                   lowerFileName.EndsWith(".mka") ||
                   lowerFileName.EndsWith(".wma");
        }

        public async Task<byte[]> ConvertToWavAsync(string filePath)
        {
            using var outputStream = new MemoryStream();
            await FFMpegArguments
                .FromFileInput(filePath)
                .OutputToPipe(new StreamPipeSink(outputStream), options => options
                    .DisableChannel(FFMpegCore.Enums.Channel.Video)
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 2 -f wav") // Ensure stereo, 16-bit PCM WAV
                    .WithFastStart())
                .ProcessAsynchronously();

            outputStream.Position = 0;
            return outputStream.ToArray();
        }

        public async Task<byte[]> ConvertToWavAsync(byte[] sourceAudioData)
        {
            using var inputStream = new MemoryStream(sourceAudioData); // Pass byte array directly
            using var outputStream = new MemoryStream();
            // inputStream.Write(sourceAudioData, 0, sourceAudioData.Length); // Not needed if MemoryStream constructor used
            // inputStream.Position = 0; // Not needed if MemoryStream constructor used

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
            return outputStream.ToArray();
        }

        public string GetAudioPath(Update e)
        {
            // This method relies on static Env.WorkDir and a specific directory structure.
            // For a more robust service, Env.WorkDir should be injected configuration.
            if (e?.Message == null) throw new ArgumentNullException(nameof(e.Message));
            try
            {
                // TODO: Replace Env.WorkDir with injected configuration
                var dirPath = Path.Combine(Env.WorkDir, "Audios", $"{e.Message.Chat.Id}");
                var files = Directory.GetFiles(dirPath, $"{e.Message.MessageId}.*");
                if (files.Length == 0)
                {
                    throw new CannotGetAudioException($"No audio file found for MessageId {e.Message.MessageId} in ChatId {e.Message.Chat.Id}");
                }
                return files.FirstOrDefault();
            }
            catch (NullReferenceException ex)
            {
                throw new CannotGetAudioException("Error accessing message details for GetAudioPath.", ex);
            }
            catch (DirectoryNotFoundException)
            {
                 throw new CannotGetAudioException($"Audio directory not found for MessageId {e.Message.MessageId} in ChatId {e.Message.Chat.Id}");
            }
        }

        public async Task<byte[]> GetAudioAsync(Update e)
        {
            var filePath = GetAudioPath(e); // Relies on the above method
            if (string.IsNullOrEmpty(filePath))
            {
                 throw new CannotGetAudioException("Audio file path was null or empty.");
            }
            return await File.ReadAllBytesAsync(filePath);
        }

        public async Task<(string FileName, byte[] FileData)> DownloadAudioAsync(Update e)
        {
            // The ITelegramBotClient dependency is now injected via the constructor and used by this method (this._botClient).
            if (e?.Message == null) throw new ArgumentNullException(nameof(e.Message));

            string fileId = string.Empty;
            string determinedFileName = string.Empty;

            if (e.Message.Audio is { } audio)
            {
                fileId = audio.FileId;
                determinedFileName = !string.IsNullOrEmpty(audio.FileName) ? audio.FileName : $"{e.Message.MessageId}.oga"; // Default extension for audio
                if (string.IsNullOrEmpty(Path.GetExtension(determinedFileName))) // Ensure extension
                {
                    determinedFileName += ".oga"; // Common for Telegram audio if not specified
                }
            }
            else if (e.Message.Voice is { } voice)
            {
                fileId = voice.FileId;
                determinedFileName = $"{e.Message.MessageId}.ogg"; // Voice messages are typically ogg
            }
            else if (e.Message.Document is { } document && IsAudio(document.FileName))
            {
                fileId = document.FileId;
                determinedFileName = document.FileName;
            }
            else
            {
                throw new CannotGetAudioException("Message does not contain supported audio, voice, or audio document.");
            }

            using (var memoryStream = new MemoryStream())
            {
                // TODO: Replace Env.IsLocalAPI, Env.SameServer, Env.BaseUrl with injected configuration
                if (Env.IsLocalAPI)
                {
                    var fileInfo = await _botClient.GetFileAsync(fileId); // Use injected _botClient
                    if (string.IsNullOrEmpty(fileInfo.FilePath)) throw new Exception("Telegram API did not return a file path.");

                    if (Env.SameServer)
                    {
                        using (var fileStream = new FileStream(fileInfo.FilePath, FileMode.Open, FileAccess.Read))
                        {
                            await fileStream.CopyToAsync(memoryStream);
                        }
                    }
                    else
                    {
                        // Ensure _httpClient is initialized if this path is taken.
                        // It's initialized in constructor for now.
                        using (var clistream = await _httpClient.GetStreamAsync($"{Env.BaseUrl}/{fileInfo.FilePath}")) // Prepend / if BaseUrl doesn't have it
                        {
                            await clistream.CopyToAsync(memoryStream);
                        }
                    }
                }
                else
                {
                    var fileInfo = await _botClient.GetFileAsync(fileId); // Use injected _botClient
                    if (string.IsNullOrEmpty(fileInfo.FilePath)) throw new Exception("Telegram API did not return a file path.");
                    
                    await _botClient.DownloadFile(fileInfo.FilePath, memoryStream); // Try without Async suffix
                }
                memoryStream.Position = 0; // Reset stream position before returning array
                return (determinedFileName, memoryStream.ToArray());
            }
        }
    }
}
