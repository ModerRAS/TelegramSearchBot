using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using File = System.IO.File;

namespace TelegramSearchBot.Handler.Download
{
    public class DownloadAudioRequest : IRequest<Unit>
    {
        public Update Update { get; }
        public DownloadAudioRequest(Update update)
        {
            Update = update;
        }
    }

    public class DownloadAudioRequestHandler : IRequestHandler<DownloadAudioRequest, Unit>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadAudioRequestHandler> _logger;
        private readonly string _audioDirectory = Path.Combine(Env.WorkDir, "Audios");

        public DownloadAudioRequestHandler(ITelegramBotClient botClient, ILogger<DownloadAudioRequestHandler> logger)
        {
            _botClient = botClient;
            _logger = logger;
        }

        private static void CreateDirectoryRecursively(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Console.WriteLine($"已创建文件夹：{path}");
            }
            string parentDirectory = Directory.GetParent(path)?.FullName;
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                CreateDirectoryRecursively(parentDirectory);
            }
        }

        public async Task<Unit> Handle(DownloadAudioRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            try
            {
                var (AudioName, AudioByte) = await IProcessAudio.DownloadAudio(_botClient, e);
                var chatid = e.Message.Chat.Id;
                var FilePath = Path.Combine(_audioDirectory, $"{chatid}");
                if (!Directory.Exists(FilePath))
                {
                    CreateDirectoryRecursively(FilePath);
                }
                await File.WriteAllBytesAsync(Path.Combine(FilePath, AudioName), AudioByte, cancellationToken);
                _logger.LogInformation($"Already Save Audio：{chatid}\t{AudioName}");
            }
            catch (Exception ex) when (
                  ex is CannotGetAudioException ||
                  ex is DirectoryNotFoundException
                  )
            {
                // _logger.LogInformation($"Cannot Save Audio: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }
            return Unit.Value;
        }
    }
} 