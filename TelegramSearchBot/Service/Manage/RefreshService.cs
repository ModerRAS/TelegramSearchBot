using System;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using System.IO;
using Newtonsoft.Json;
using TelegramSearchBot.Model;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.AI.ASR;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.AI.QR;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Manage
{
    public class RefreshService : MessageService, IService
    {
        public new string ServiceName => "RefreshService";
        private readonly ILogger<RefreshService> _logger;
        private readonly ChatImportService _chatImport;
        private readonly AutoASRService _autoASRService;
        private readonly MessageExtensionService _messageExtensionService;
        private readonly PaddleOCRService _paddleOCRService;
        private readonly AutoQRService _autoQRService;
        private readonly GeneralLLMService _generalLLMService;

        public RefreshService(ILogger<RefreshService> logger,
                            LuceneManager lucene,
                            SendMessage Send,
                            DataDbContext context,
                            ChatImportService chatImport,
                            AutoASRService autoASRService,
                            MessageExtensionService messageExtensionService,
                            PaddleOCRService paddleOCRService,
                            AutoQRService autoQRService,
                            GeneralLLMService generalLLMService) : base(logger, lucene, Send, context)
        {
            _logger = logger;
            _chatImport = chatImport;
            _autoASRService = autoASRService;
            _messageExtensionService = messageExtensionService;
            _paddleOCRService = paddleOCRService;
            _autoQRService = autoQRService;
            _generalLLMService = generalLLMService;
        }

        private async Task RebuildIndex()
        {
            var dirs = new List<string>();
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data_*"));
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data"));
            await Send.Log($"找到{dirs.Count}个索引目录，现在开始清空目录");
            foreach (var dir in dirs)
            {
                Directory.Delete(dir, true);
                await Send.Log($"删除了{dir}");
            }
            await Send.Log($"删除完成");
            var Messages = DataContext.Messages;
            long count = Messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始重建索引");
            lucene.WriteDocuments(Messages.ToList());
            await Send.Log($"重建完成");
        }

        private async Task ImportAll()
        {
            await Send.Log("开始导入数据库内容");
            var importModel = JsonConvert.DeserializeObject<ExportModel>(await File.ReadAllTextAsync("/tmp/export.json"));
            await DataContext.UsersWithGroup.AddRangeAsync(importModel.Users);
            await DataContext.Messages.AddRangeAsync(importModel.Messages);
            await DataContext.SaveChangesAsync();
            await Send.Log("导入完成");
        }

        private async Task CopyLiteDbToSqlite()
        {
            var Messages = Env.Database.GetCollection<Message>("Messages").FindAll();
            long count = Messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始迁移数据至Sqlite");
            var number = 0;
            foreach (var e in Messages)
            {
                number++;
                if (number % 10000 == 0)
                {
                    await Send.Log($"已迁移{number}条数据");
                }
                var sqliteMessage = from sq in DataContext.Messages
                                    where sq.MessageId == e.MessageId &&
                                          sq.GroupId == e.GroupId &&
                                          sq.Content.Equals(e.Content)
                                    select sq;
                if (sqliteMessage.FirstOrDefault() != null)
                {
                    continue;
                }

                await DataContext.Messages.AddAsync(new Message()
                {
                    DateTime = e.DateTime,
                    Content = e.Content,
                    GroupId = e.GroupId,
                    MessageId = e.MessageId,
                });
                await DataContext.SaveChangesAsync();
            }
            await DataContext.SaveChangesAsync();
            await Send.Log($"已迁移{number}条数据，迁移完成");
        }

        private async Task ScanAndProcessAudioFiles()
        {
            var audioDir = Path.Combine(Env.WorkDir, "Audios");
            if (!Directory.Exists(audioDir))
            {
                await Send.Log($"音频目录不存在: {audioDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(audioDir);
            foreach (var chatDir in chatDirs)
            {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var audioFiles = Directory.GetFiles(chatDir);

                foreach (var audioFile in audioFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(audioFile);
                    if (long.TryParse(fileName, out var messageId))
                    {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue)
                        {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);
                            if (!extensions.Any(x => x.Name == "ASR_Result"))
                            {
                                await Send.Log($"开始处理音频: {chatId}/{messageId}");
                                try
                                {
                                    var asrResult = await _autoASRService.ExecuteAsync(audioFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                    await Send.Log($"成功处理音频: {chatId}/{messageId}");
                                }
                                catch (Exception ex)
                                {
                                    await Send.Log($"处理图片QR码失败: {chatId}/{messageId}, 错误: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task ScanAndProcessImageFiles() {
            var imageDir = Path.Combine(Env.WorkDir, "Photos");
            if (!Directory.Exists(imageDir)) {
                await Send.Log($"图片目录不存在: {imageDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(imageDir);
            foreach (var chatDir in chatDirs) {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var imageFiles = Directory.GetFiles(chatDir);

                foreach (var imageFile in imageFiles) {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile);
                    if (long.TryParse(fileName, out var messageId)) {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue) {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);

                            // 处理OCR
                            if (!extensions.Any(x => x.Name == "OCR_Result")) {
                                await Send.Log($"开始处理图片OCR: {chatId}/{messageId}");
                                try {
                                    var ocrResult = await _paddleOCRService.ExecuteAsync(new MemoryStream(await File.ReadAllBytesAsync(imageFile)));
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "OCR_Result", ocrResult);
                                    if (!string.IsNullOrEmpty(ocrResult)) {
                                        await Send.Log($"成功处理图片OCR: {chatId}/{messageId}");
                                    } else {
                                        await Send.Log($"图片OCR处理失败或未找到文本: {chatId}/{messageId}");
                                    }
                                } catch (Exception ex) {
                                    await Send.Log($"处理图片OCR失败: {chatId}/{messageId}, 错误: {ex.Message}");
                                }
                            }

                            // 处理QR码
                            if (!extensions.Any(x => x.Name == "QR_Result")) {
                                await Send.Log($"开始处理图片QR码: {chatId}/{messageId}");
                                try {
                                    var qrResult = await _autoQRService.ExecuteAsync(imageFile);
                                    if (!string.IsNullOrEmpty(qrResult)) {
                                        await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "QR_Result", qrResult);
                                        await Send.Log($"成功处理图片QR码: {chatId}/{messageId}");
                                    }
                                } catch (Exception ex) {
                                    await Send.Log($"处理图片QR码失败: {chatId}/{messageId}, 错误: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task ScanAndProcessVideoFiles()
        {
            var videoDir = Path.Combine(Env.WorkDir, "Videos");
            if (!Directory.Exists(videoDir))
            {
                await Send.Log($"视频目录不存在: {videoDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(videoDir);
            foreach (var chatDir in chatDirs)
            {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var videoFiles = Directory.GetFiles(chatDir);

                foreach (var videoFile in videoFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(videoFile);
                    if (long.TryParse(fileName, out var messageId))
                    {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue)
                        {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);
                            if (!extensions.Any(x => x.Name == "ASR_Result"))
                            {
                                await Send.Log($"开始处理视频: {chatId}/{messageId}");
                                try
                                {
                                    var asrResult = await _autoASRService.ExecuteAsync(videoFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                    await Send.Log($"成功处理视频: {chatId}/{messageId}");
                                }
                                catch (Exception ex)
                                {
                                    await Send.Log($"处理视频失败: {chatId}/{messageId}, 错误: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task ExecuteAsync(string Command)
        {
            if (Command.Equals("重建索引"))
            {
                await RebuildIndex();
            }
            if (Command.Equals("导入数据"))
            {
                await ImportAll();
            }
            if (Command.Equals("迁移数据"))
            {
                await CopyLiteDbToSqlite();
            }
            if (Command.Equals("导入聊天记录"))
            {
                await _chatImport.ExecuteAsync("导入聊天记录");
            }
            if (Command.Equals("扫描音频文件"))
            {
                await ScanAndProcessAudioFiles();
            }
            if (Command.Equals("扫描图片文件"))
            {
                await ScanAndProcessImageFiles();
            }
            if (Command.Equals("扫描视频文件"))
            {
                await ScanAndProcessVideoFiles();
            }
            if (Command.Equals("扫描图片Alt"))
            {
                await ScanAndProcessAltImageFiles();
            }
        }

        private async Task ScanAndProcessAltImageFiles()
        {
            var imageDir = Path.Combine(Env.WorkDir, "Photos");
            if (!Directory.Exists(imageDir))
            {
                await Send.Log($"图片目录不存在: {imageDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(imageDir);
            foreach (var chatDir in chatDirs)
            {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var imageFiles = Directory.GetFiles(chatDir);

                foreach (var imageFile in imageFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile);
                    if (long.TryParse(fileName, out var messageId))
                    {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue)
                        {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);

                            // 处理Alt信息
                            if (!extensions.Any(x => x.Name == "Alt_Result"))
                            {
                                await Send.Log($"开始处理图片Alt: {chatId}/{messageId}");
                                try
                                {
                                    var imageBytes = await File.ReadAllBytesAsync(imageFile);
                                    var altResult = await _generalLLMService.AnalyzeImageAsync(imageBytes, chatId);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "Alt_Result", altResult);
                                    if (!string.IsNullOrEmpty(altResult))
                                    {
                                        await Send.Log($"成功处理图片Alt: {chatId}/{messageId}");
                                    }
                                    else
                                    {
                                        await Send.Log($"图片Alt处理失败或未生成描述: {chatId}/{messageId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    await Send.Log($"处理图片Alt失败: {chatId}/{messageId}, 错误: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

    }
}
