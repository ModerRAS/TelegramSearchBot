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
    public class DownloadVideoRequest : IRequest<Unit>
    {
        public Update Update { get; }
        public DownloadVideoRequest(Update update)
        {
            Update = update;
        }
    }

    public class DownloadVideoRequestHandler : IRequestHandler<DownloadVideoRequest, Unit>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadVideoRequestHandler> _logger;
        private readonly string _videoDirectory = Path.Combine(Env.WorkDir, "Videos");

        public DownloadVideoRequestHandler(ITelegramBotClient botClient, ILogger<DownloadVideoRequestHandler> logger)
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

        public async Task<Unit> Handle(DownloadVideoRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            try
            {
                var (VideoName, VideoByte) = await IProcessVideo.DownloadVideo(_botClient, e);
                var chatid = e.Message.Chat.Id;
                var FilePath = Path.Combine(_videoDirectory, $"{chatid}");
                if (!Directory.Exists(FilePath))
                {
                    CreateDirectoryRecursively(FilePath);
                }
                await File.WriteAllBytesAsync(Path.Combine(FilePath, VideoName), VideoByte, cancellationToken);
                _logger.LogInformation($"Already Save Video：{chatid}\t{VideoName}\t{VideoByte.Length / 1048576}MiB");
            }
            catch (Exception ex) when (
                  ex is CannotGetVideoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                // _logger.LogInformation($"Cannot Save Video: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }
            return Unit.Value;
        }
    }
} 