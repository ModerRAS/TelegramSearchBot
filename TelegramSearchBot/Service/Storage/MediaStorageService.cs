using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interfaces;
using File = System.IO.File;

namespace TelegramSearchBot.Service.Storage
{
    public class MediaStorageService : IMediaStorageService
    {
        private readonly ILogger<MediaStorageService> _logger;
        private readonly string _audioDirectory;
        private readonly string _videoDirectory;
        private readonly string _photoDirectory;

        public MediaStorageService(ILogger<MediaStorageService> logger)
        {
            _logger = logger;
            _audioDirectory = Path.Combine(Env.WorkDir, "Audios");
            _videoDirectory = Path.Combine(Env.WorkDir, "Videos");
            _photoDirectory = Path.Combine(Env.WorkDir, "Photos");

            // 确保基础目录存在
            EnsureDirectoryExists(_audioDirectory);
            EnsureDirectoryExists(_videoDirectory);
            EnsureDirectoryExists(_photoDirectory);
        }

        public async Task<string> SaveAudioAsync(long chatId, int messageId, string fileName, byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
            {
                throw new ArgumentException("文件数据不能为空", nameof(fileData));
            }

            // 确保用户目录存在
            var chatDirectory = Path.Combine(_audioDirectory, chatId.ToString());
            EnsureDirectoryExists(chatDirectory);

            // 如果没有提供文件名，使用消息ID作为文件名
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{messageId}.audio";
            }

            var filePath = Path.Combine(chatDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileData);
            _logger.LogInformation($"已保存音频文件: {filePath}, 大小: {fileData.Length / 1024}KB");
            
            return filePath;
        }

        public async Task<string> SaveVideoAsync(long chatId, int messageId, string fileName, byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
            {
                throw new ArgumentException("文件数据不能为空", nameof(fileData));
            }

            // 确保用户目录存在
            var chatDirectory = Path.Combine(_videoDirectory, chatId.ToString());
            EnsureDirectoryExists(chatDirectory);

            // 如果没有提供文件名，使用消息ID作为文件名
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{messageId}.video";
            }

            var filePath = Path.Combine(chatDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileData);
            _logger.LogInformation($"已保存视频文件: {filePath}, 大小: {fileData.Length / 1048576}MB");
            
            return filePath;
        }

        public async Task<string> SavePhotoAsync(long chatId, int messageId, string fileName, byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
            {
                throw new ArgumentException("文件数据不能为空", nameof(fileData));
            }

            // 确保用户目录存在
            var chatDirectory = Path.Combine(_photoDirectory, chatId.ToString());
            EnsureDirectoryExists(chatDirectory);

            // 如果没有提供文件名，使用消息ID作为文件名
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{messageId}.photo";
            }

            var filePath = Path.Combine(chatDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileData);
            _logger.LogInformation($"已保存图片文件: {filePath}, 大小: {fileData.Length / 1024}KB");
            
            return filePath;
        }

        public string GetAudioPath(Update update)
        {
            if (update?.Message == null)
            {
                throw new ArgumentNullException(nameof(update.Message));
            }

            try
            {
                var dirPath = Path.Combine(_audioDirectory, $"{update.Message.Chat.Id}");
                var files = Directory.GetFiles(dirPath, $"{update.Message.MessageId}.*");
                if (files.Length == 0)
                {
                    throw new CannotGetAudioException($"没有找到音频文件：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
                }
                return files.FirstOrDefault();
            }
            catch (NullReferenceException ex)
            {
                throw new CannotGetAudioException("获取音频路径时发生错误", ex);
            }
            catch (DirectoryNotFoundException)
            {
                throw new CannotGetAudioException($"未找到音频目录：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
            }
        }

        public string GetVideoPath(Update update)
        {
            if (update?.Message == null)
            {
                throw new ArgumentNullException(nameof(update.Message));
            }

            try
            {
                var dirPath = Path.Combine(_videoDirectory, $"{update.Message.Chat.Id}");
                var files = Directory.GetFiles(dirPath, $"{update.Message.MessageId}.*");
                if (files.Length == 0)
                {
                    throw new CannotGetVideoException($"没有找到视频文件：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
                }
                return files.FirstOrDefault();
            }
            catch (NullReferenceException ex)
            {
                throw new CannotGetVideoException("获取视频路径时发生错误", ex);
            }
            catch (DirectoryNotFoundException)
            {
                throw new CannotGetVideoException($"未找到视频目录：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
            }
        }

        public string GetPhotoPath(Update update)
        {
            if (update?.Message == null)
            {
                throw new ArgumentNullException(nameof(update.Message));
            }

            try
            {
                var dirPath = Path.Combine(_photoDirectory, $"{update.Message.Chat.Id}");
                var files = Directory.GetFiles(dirPath, $"{update.Message.MessageId}.*");
                if (files.Length == 0)
                {
                    throw new CannotGetPhotoException($"没有找到图片文件：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
                }
                return files.FirstOrDefault();
            }
            catch (NullReferenceException ex)
            {
                throw new CannotGetPhotoException("获取图片路径时发生错误", ex);
            }
            catch (DirectoryNotFoundException)
            {
                throw new CannotGetPhotoException($"未找到图片目录：MessageId {update.Message.MessageId}, ChatId {update.Message.Chat.Id}");
            }
        }

        public async Task<byte[]> GetAudioDataAsync(Update update)
        {
            var filePath = GetAudioPath(update);
            if (string.IsNullOrEmpty(filePath))
            {
                throw new CannotGetAudioException("音频文件路径为空");
            }
            return await File.ReadAllBytesAsync(filePath);
        }

        public async Task<byte[]> GetVideoDataAsync(Update update)
        {
            var filePath = GetVideoPath(update);
            if (string.IsNullOrEmpty(filePath))
            {
                throw new CannotGetVideoException("视频文件路径为空");
            }
            return await File.ReadAllBytesAsync(filePath);
        }

        public async Task<byte[]> GetPhotoDataAsync(Update update)
        {
            var filePath = GetPhotoPath(update);
            if (string.IsNullOrEmpty(filePath))
            {
                throw new CannotGetPhotoException("图片文件路径为空");
            }
            return await File.ReadAllBytesAsync(filePath);
        }

        public void EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                throw new ArgumentException("目录路径不能为空", nameof(directoryPath));
            }

            if (!Directory.Exists(directoryPath))
            {
                try
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogInformation($"已创建目录: {directoryPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"创建目录失败: {directoryPath}");
                    throw;
                }
            }
        }
    }
} 