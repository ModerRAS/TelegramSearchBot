using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using File = System.IO.File;

namespace TelegramSearchBot.Controller.Download
{
    public class DownloadPhotoController : IOnUpdate
    {
        public ITelegramBotClient botClient { get; private set; }
        public string PhotoDirectory { get; private set; } = Path.Combine(Env.WorkDir, "Photos");

        public List<Type> Dependencies => new List<Type>();

        private readonly ILogger<DownloadPhotoController> logger;
        public DownloadPhotoController(ITelegramBotClient botClient, ILogger<DownloadPhotoController> logger)
        {
            this.botClient = botClient;
            this.logger = logger;
        }
        void CreateDirectoryRecursively(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                logger.LogInformation("已创建文件夹：{Path}", path);
            }

            // 获取父文件夹路径
            string parentDirectory = Directory.GetParent(path)?.FullName;

            // 递归调用直到创建完所有文件夹
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                CreateDirectoryRecursively(parentDirectory);
            }
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            var e = p.Update;
            try
            {
                var (PhotoName, PhotoByte) = await IProcessPhoto.DownloadPhoto(botClient, e);
                var chatid = e.Message.Chat.Id;
                var FilePath = Path.Combine(PhotoDirectory, $"{chatid}");
                if (!Directory.Exists(FilePath))
                {
                    CreateDirectoryRecursively(FilePath);
                }
                await File.WriteAllBytesAsync(Path.Combine(FilePath, PhotoName), PhotoByte);
                logger.LogInformation($"Already Save Photo：{chatid}\t{PhotoName}");
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                //logger.LogInformation($"Cannot Save Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

        }
    }
}
