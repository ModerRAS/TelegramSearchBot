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
using TelegramSearchBot.Service.Vector;
using MediatR;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.AI.ASR;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Manage
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class RefreshService : MessageService, IService
    {
        public new string ServiceName => "RefreshService";
        private readonly ILogger<RefreshService> _logger;
        private readonly ChatImportService _chatImport;
        private readonly IAutoASRService _autoASRService;
        private readonly MessageExtensionService _messageExtensionService;
        private readonly IPaddleOCRService _paddleOCRService;
        private readonly AutoQRService _autoQRService;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly IMediator _mediator;

        public RefreshService(ILogger<RefreshService> logger,
                            LuceneManager lucene,
                            SendMessage Send,
                            DataDbContext context,
                            VectorGenerationService vectorGenerationService,
                            ChatImportService chatImport,
                            IAutoASRService autoASRService,
                            MessageExtensionService messageExtensionService,
                            IPaddleOCRService paddleOCRService,
                            AutoQRService autoQRService,
                            IGeneralLLMService generalLLMService,
                            IMediator mediator) : base(logger, lucene, Send, context, mediator)
        {
            _logger = logger;
            _chatImport = chatImport;
            _autoASRService = autoASRService;
            _messageExtensionService = messageExtensionService;
            _paddleOCRService = paddleOCRService;
            _autoQRService = autoQRService;
            _generalLLMService = generalLLMService;
            _mediator = mediator;
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

        private async Task ScanAndProcessAudioFiles()
        {
            var audioDir = Path.Combine(Env.WorkDir, "Audios");
            if (!Directory.Exists(audioDir))
            {
                await Send.Log($"音频目录不存在: {audioDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(audioDir);
            var allAudioFiles = chatDirs.SelectMany(dir => Directory.GetFiles(dir)).ToList();
            long totalFiles = allAudioFiles.Count;
            long processedFiles = 0;
            long nextPercent = 1;
            long filesPerPercent = totalFiles / 100;

            await Send.Log($"开始处理音频文件，共{totalFiles}个文件");

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
                                try
                                {
                                    var asrResult = await _autoASRService.ExecuteAsync(audioFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"处理音频失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent)
                    {
                        await Send.Log($"音频处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"音频处理完成: 100% ({totalFiles}/{totalFiles})");
        }

        private async Task ScanAndProcessImageFiles() {
            var imageDir = Path.Combine(Env.WorkDir, "Photos");
            if (!Directory.Exists(imageDir)) {
                await Send.Log($"图片目录不存在: {imageDir}");
                return;
            }

            var chatDirs = Directory.GetDirectories(imageDir);
            var allImageFiles = chatDirs.SelectMany(dir => Directory.GetFiles(dir)).ToList();
            long totalFiles = allImageFiles.Count;
            long processedFiles = 0;
            long nextPercent = 1;
            long filesPerPercent = totalFiles / 100;

            await Send.Log($"开始处理图片文件，共{totalFiles}个文件");

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
                                try {
                                    var ocrResult = await _paddleOCRService.ExecuteAsync(new MemoryStream(await File.ReadAllBytesAsync(imageFile)));
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "OCR_Result", ocrResult);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"处理图片OCR失败: {chatId}/{messageId}");
                                }
                            }

                            // 处理QR码
                            if (!extensions.Any(x => x.Name == "QR_Result")) {
                                try {
                                    var qrResult = await _autoQRService.ExecuteAsync(imageFile);
                                    if (!string.IsNullOrEmpty(qrResult)) {
                                        await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "QR_Result", qrResult);
                                    }
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"处理图片QR码失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent) {
                        await Send.Log($"图片处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"图片处理完成: 100% ({totalFiles}/{totalFiles})");
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
            var allVideoFiles = chatDirs.SelectMany(dir => Directory.GetFiles(dir)).ToList();
            long totalFiles = allVideoFiles.Count;
            long processedFiles = 0;
            long nextPercent = 1;
            long filesPerPercent = totalFiles / 100;

            await Send.Log($"开始处理视频文件，共{totalFiles}个文件");

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
                                try
                                {
                                    var asrResult = await _autoASRService.ExecuteAsync(videoFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"处理视频失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent)
                    {
                        await Send.Log($"视频处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"视频处理完成: 100% ({totalFiles}/{totalFiles})");
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
            var allImageFiles = chatDirs.SelectMany(dir => Directory.GetFiles(dir)).ToList();
            long totalFiles = allImageFiles.Count;
            long processedFiles = 0;
            long nextPercent = 1;
            long filesPerPercent = totalFiles / 100;

            await Send.Log($"开始处理图片Alt信息，共{totalFiles}个文件");

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
                                try
                                {
                                    var altResult = await _generalLLMService.AnalyzeImageAsync(imageFile, chatId);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "Alt_Result", altResult);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"处理图片Alt失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent)
                    {
                        await Send.Log($"图片Alt处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"图片Alt处理完成: 100% ({totalFiles}/{totalFiles})");
        }

    }
}
