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
    public class DownloadPhotoRequest : IRequest<Unit>
    {
        public Update Update { get; }
        public DownloadPhotoRequest(Update update)
        {
            Update = update;
        }
    }

    public class DownloadPhotoRequestHandler : IRequestHandler<DownloadPhotoRequest, Unit>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<DownloadPhotoRequestHandler> _logger;
        private readonly string _photoDirectory = Path.Combine(Env.WorkDir, "Photos");

        public DownloadPhotoRequestHandler(ITelegramBotClient botClient, ILogger<DownloadPhotoRequestHandler> logger)
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

        public async Task<Unit> Handle(DownloadPhotoRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            try
            {
                var (PhotoName, PhotoByte) = await IProcessPhoto.DownloadPhoto(_botClient, e);
                var chatid = e.Message.Chat.Id;
                var FilePath = Path.Combine(_photoDirectory, $"{chatid}");
                if (!Directory.Exists(FilePath))
                {
                    CreateDirectoryRecursively(FilePath);
                }
                await File.WriteAllBytesAsync(Path.Combine(FilePath, PhotoName), PhotoByte, cancellationToken);
                _logger.LogInformation($"Already Save Photo：{chatid}\t{PhotoName}");
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                // _logger.LogInformation($"Cannot Save Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }
            return Unit.Value;
        }
    }
} 