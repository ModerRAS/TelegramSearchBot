using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.ASR;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.ASR;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.AI.QR;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Search.Tool;
using TelegramSearchBot.Helper;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class RefreshService : MessageService, IService {
        public new string ServiceName => "RefreshService";
        private readonly ILogger<RefreshService> _logger;
        private readonly ChatImportService _chatImport;
        private readonly IAutoASRService _autoASRService;
        private readonly MessageExtensionService _messageExtensionService;
        private readonly IPaddleOCRService _paddleOCRService;
        private readonly AutoQRService _autoQRService;
    private readonly IGeneralLLMService _generalLLMService;
        private readonly FaissVectorService _faissVectorService;
        private readonly ConversationSegmentationService _conversationSegmentationService;

        public RefreshService(ILogger<RefreshService> logger,
                            LuceneManager lucene,
                            SendMessage Send,
                            DataDbContext context,
                            ChatImportService chatImport,
                            IAutoASRService autoASRService,
                            MessageExtensionService messageExtensionService,
                            IPaddleOCRService paddleOCRService,
                            AutoQRService autoQRService,
                            IGeneralLLMService generalLLMService,
                            IMediator mediator,
                            FaissVectorService faissVectorService,
                            ConversationSegmentationService conversationSegmentationService) : base(logger, lucene, Send, context, mediator) {
            _logger = logger;
            _chatImport = chatImport;
            _autoASRService = autoASRService;
            _messageExtensionService = messageExtensionService;
            _paddleOCRService = paddleOCRService;
            _autoQRService = autoQRService;
            _generalLLMService = generalLLMService;
            _faissVectorService = faissVectorService;
            _conversationSegmentationService = conversationSegmentationService;
        }

        private async Task RebuildIndex() {
            var dirs = new List<string>();
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data_*"));
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data"));
            await Send.Log($"找到{dirs.Count}个索引目录，现在开始清空目录");
            foreach (var dir in dirs) {
                Directory.Delete(dir, true);
                await Send.Log($"删除了{dir}");
            }
            await Send.Log($"删除完成");
            var Messages = DataContext.Messages;
            long count = Messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始重建索引");
            lucene.WriteDocuments(MessageDtoMapper.ToDtoList(Messages.ToList()));
            await Send.Log($"重建完成");
        }

        private async Task ImportAll() {
            await Send.Log("开始导入数据库内容");
            var importModel = JsonConvert.DeserializeObject<ExportModel>(await File.ReadAllTextAsync("/tmp/export.json"));
            await DataContext.UsersWithGroup.AddRangeAsync(importModel.Users);
            await DataContext.Messages.AddRangeAsync(importModel.Messages);
            await DataContext.SaveChangesAsync();
            await Send.Log("导入完成");
        }

        private async Task ScanAndProcessAudioFiles() {
            var audioDir = Path.Combine(Env.WorkDir, "Audios");
            if (!Directory.Exists(audioDir)) {
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

            foreach (var chatDir in chatDirs) {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var audioFiles = Directory.GetFiles(chatDir);

                foreach (var audioFile in audioFiles) {
                    var fileName = Path.GetFileNameWithoutExtension(audioFile);
                    if (long.TryParse(fileName, out var messageId)) {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue) {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);
                            if (!extensions.Any(x => x.Name == "ASR_Result")) {
                                try {
                                    var asrResult = await _autoASRService.ExecuteAsync(audioFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"处理音频失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent) {
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

        private async Task ScanAndProcessVideoFiles() {
            var videoDir = Path.Combine(Env.WorkDir, "Videos");
            if (!Directory.Exists(videoDir)) {
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

            foreach (var chatDir in chatDirs) {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var videoFiles = Directory.GetFiles(chatDir);

                foreach (var videoFile in videoFiles) {
                    var fileName = Path.GetFileNameWithoutExtension(videoFile);
                    if (long.TryParse(fileName, out var messageId)) {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue) {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);
                            if (!extensions.Any(x => x.Name == "ASR_Result")) {
                                try {
                                    var asrResult = await _autoASRService.ExecuteAsync(videoFile);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "ASR_Result", asrResult);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"处理视频失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent) {
                        await Send.Log($"视频处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"视频处理完成: 100% ({totalFiles}/{totalFiles})");
        }

        public async Task ExecuteAsync(string Command) {
            if (Command.Equals("重建索引")) {
                await RebuildIndex();
            }
            if (Command.Equals("导入数据")) {
                await ImportAll();
            }
            if (Command.Equals("导入聊天记录")) {
                await _chatImport.ExecuteAsync("导入聊天记录");
            }
            if (Command.Equals("扫描音频文件")) {
                await ScanAndProcessAudioFiles();
            }
            if (Command.Equals("扫描图片文件")) {
                await ScanAndProcessImageFiles();
            }
            if (Command.Equals("扫描视频文件")) {
                await ScanAndProcessVideoFiles();
            }
            if (Command.Equals("扫描图片Alt")) {
                await ScanAndProcessAltImageFiles();
            }
            if (Command.Equals("清理向量数据")) {
                await ClearAllVectorData();
            }
            if (Command.Equals("重新向量化")) {
                await RegenerateAndVectorizeSegments();
            }
            if (Command.Equals("重建向量索引")) {
                await RebuildVectorIndex();
            }
            if (Command.StartsWith("重建群组向量索引:")) {
                var groupIdStr = Command.Replace("重建群组向量索引:", "").Trim();
                if (long.TryParse(groupIdStr, out var groupId)) {
                    await RebuildVectorIndexForGroup(groupId);
                } else {
                    await Send.Log($"无效的群组ID: {groupIdStr}");
                }
            }
            if (Command.StartsWith("调试向量搜索:")) {
                var parts = Command.Replace("调试向量搜索:", "").Trim().Split('|');
                if (parts.Length >= 2 && long.TryParse(parts[0], out var groupId)) {
                    var searchQuery = parts[1];
                    await DebugVectorSearch(groupId, searchQuery);
                } else {
                    await Send.Log("使用格式: 调试向量搜索:群组ID|搜索关键词");
                }
            }
        }

        private async Task ScanAndProcessAltImageFiles() {
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

            await Send.Log($"开始处理图片Alt信息，共{totalFiles}个文件");

            foreach (var chatDir in chatDirs) {
                var chatId = long.Parse(Path.GetFileName(chatDir));
                var imageFiles = Directory.GetFiles(chatDir);

                foreach (var imageFile in imageFiles) {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile);
                    if (long.TryParse(fileName, out var messageId)) {
                        var messageDataId = await _messageExtensionService.GetMessageIdByMessageIdAndGroupId(messageId, chatId);
                        if (messageDataId.HasValue) {
                            var extensions = await _messageExtensionService.GetByMessageDataIdAsync(messageDataId.Value);

                            // 处理Alt信息
                            if (!extensions.Any(x => x.Name == "Alt_Result")) {
                                try {
                                    var altResult = await _generalLLMService.AnalyzeImageAsync(imageFile, chatId);
                                    await _messageExtensionService.AddOrUpdateAsync(messageDataId.Value, "Alt_Result", altResult);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, $"处理图片Alt失败: {chatId}/{messageId}");
                                }
                            }
                        }
                    }

                    processedFiles++;
                    if (filesPerPercent > 0 && processedFiles >= nextPercent * filesPerPercent) {
                        await Send.Log($"图片Alt处理进度: {nextPercent}% ({processedFiles}/{totalFiles})");
                        nextPercent++;
                    }
                }
            }
            await Send.Log($"图片Alt处理完成: 100% ({totalFiles}/{totalFiles})");
        }

        /// <summary>
        /// 清理所有向量数据
        /// </summary>
        private async Task ClearAllVectorData() {
            await Send.Log("开始清理向量数据...");

            try {
                // 清理向量索引数据
                var vectorIndexes = DataContext.VectorIndexes.ToList();
                DataContext.VectorIndexes.RemoveRange(vectorIndexes);
                await Send.Log($"已清理 {vectorIndexes.Count} 个向量索引记录");

                // 清理FAISS索引文件记录
                var faissIndexFiles = DataContext.FaissIndexFiles.ToList();
                DataContext.FaissIndexFiles.RemoveRange(faissIndexFiles);
                await Send.Log($"已清理 {faissIndexFiles.Count} 个FAISS索引文件记录");

                // 清理对话段数据
                var conversationSegmentMessages = DataContext.ConversationSegmentMessages.ToList();
                DataContext.ConversationSegmentMessages.RemoveRange(conversationSegmentMessages);
                await Send.Log($"已清理 {conversationSegmentMessages.Count} 个对话段消息关联记录");

                var conversationSegments = DataContext.ConversationSegments.ToList();
                DataContext.ConversationSegments.RemoveRange(conversationSegments);
                await Send.Log($"已清理 {conversationSegments.Count} 个对话段记录");

                await DataContext.SaveChangesAsync();

                // 清理物理文件
                var indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
                if (Directory.Exists(indexDirectory)) {
                    var indexFiles = Directory.GetFiles(indexDirectory, "*.faiss");
                    foreach (var file in indexFiles) {
                        File.Delete(file);
                    }
                    await Send.Log($"已删除 {indexFiles.Length} 个FAISS索引文件");
                }

                await Send.Log("向量数据清理完成");
            } catch (Exception ex) {
                _logger.LogError(ex, "清理向量数据时发生错误");
                await Send.Log($"清理向量数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重新生成对话段并向量化
        /// </summary>
        private async Task RegenerateAndVectorizeSegments() {
            await Send.Log("开始重新生成对话段并向量化...");

            try {
                // 获取当前数据库中的所有群组
                var groupIds = await DataContext.Messages
                    .Select(m => m.GroupId)
                    .Distinct()
                    .ToListAsync();

                await Send.Log($"找到 {groupIds.Count} 个群组需要处理");

                long totalProcessedGroups = 0;
                long totalSegmentsGenerated = 0;
                long totalSegmentsVectorized = 0;

                foreach (var groupId in groupIds) {
                    try {
                        await Send.Log($"正在处理群组 {groupId}...");

                        // 生成对话段
                        var segments = await _conversationSegmentationService.CreateSegmentsForGroupAsync(groupId);
                        totalSegmentsGenerated += segments.Count;
                        await Send.Log($"群组 {groupId} 生成了 {segments.Count} 个对话段");

                        // 向量化对话段
                        await _faissVectorService.VectorizeGroupSegments(groupId);

                        // 统计成功向量化的对话段数量
                        var vectorizedCount = await DataContext.ConversationSegments
                            .Where(s => s.GroupId == groupId && s.IsVectorized)
                            .CountAsync();
                        totalSegmentsVectorized += vectorizedCount;

                        await Send.Log($"群组 {groupId} 成功向量化了 {vectorizedCount} 个对话段");
                        totalProcessedGroups++;

                        // 处理间隔，避免过度占用资源
                        await Task.Delay(500);
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"处理群组 {groupId} 时发生错误");
                        await Send.Log($"处理群组 {groupId} 失败: {ex.Message}");
                    }
                }

                await Send.Log($"重新向量化完成！");
                await Send.Log($"处理群组数: {totalProcessedGroups}/{groupIds.Count}");
                await Send.Log($"生成对话段: {totalSegmentsGenerated}");
                await Send.Log($"成功向量化: {totalSegmentsVectorized}");
            } catch (Exception ex) {
                _logger.LogError(ex, "重新向量化过程中发生错误");
                await Send.Log($"重新向量化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重建向量索引（清理并重新向量化）
        /// </summary>
        private async Task RebuildVectorIndex() {
            await Send.Log("=== 开始重建向量索引 ===");

            // 第一步：清理现有数据
            await ClearAllVectorData();

            // 短暂休息
            await Task.Delay(1000);

            // 第二步：重新生成和向量化
            await RegenerateAndVectorizeSegments();

            await Send.Log("=== 向量索引重建完成 ===");
        }

        /// <summary>
        /// 为指定群组重新向量化
        /// </summary>
        private async Task RebuildVectorIndexForGroup(long groupId) {
            await Send.Log($"=== 开始为群组 {groupId} 重建向量索引 ===");

            try {
                // 清理该群组的向量数据
                var vectorIndexes = DataContext.VectorIndexes.Where(vi => vi.GroupId == groupId).ToList();
                DataContext.VectorIndexes.RemoveRange(vectorIndexes);

                var faissIndexFiles = DataContext.FaissIndexFiles.Where(f => f.GroupId == groupId).ToList();
                DataContext.FaissIndexFiles.RemoveRange(faissIndexFiles);

                var segments = DataContext.ConversationSegments.Where(s => s.GroupId == groupId).ToList();
                var segmentIds = segments.Select(s => s.Id).ToList();
                var segmentMessages = DataContext.ConversationSegmentMessages.Where(sm => segmentIds.Contains(sm.ConversationSegmentId)).ToList();

                DataContext.ConversationSegmentMessages.RemoveRange(segmentMessages);
                DataContext.ConversationSegments.RemoveRange(segments);

                await DataContext.SaveChangesAsync();
                await Send.Log($"已清理群组 {groupId} 的向量数据");

                // 清理物理文件
                var indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
                var groupIndexFile = Path.Combine(indexDirectory, $"{groupId}_ConversationSegment.faiss");
                if (File.Exists(groupIndexFile)) {
                    File.Delete(groupIndexFile);
                    await Send.Log($"已删除群组 {groupId} 的索引文件");
                }

                // 重新生成对话段
                var newSegments = await _conversationSegmentationService.CreateSegmentsForGroupAsync(groupId);
                await Send.Log($"群组 {groupId} 生成了 {newSegments.Count} 个对话段");

                // 向量化对话段
                await _faissVectorService.VectorizeGroupSegments(groupId);

                var vectorizedCount = await DataContext.ConversationSegments
                    .Where(s => s.GroupId == groupId && s.IsVectorized)
                    .CountAsync();

                await Send.Log($"群组 {groupId} 向量索引重建完成，成功向量化 {vectorizedCount} 个对话段");
            } catch (Exception ex) {
                _logger.LogError(ex, $"重建群组 {groupId} 向量索引时发生错误");
                await Send.Log($"群组 {groupId} 向量索引重建失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 调试向量搜索功能
        /// </summary>
        private async Task DebugVectorSearch(long groupId, string searchQuery) {
            await Send.Log($"=== 调试向量搜索 群组:{groupId} 查询:{searchQuery} ===");

            try {
                // 1. 检查群组的对话段数据
                var segments = await DataContext.ConversationSegments
                    .Where(s => s.GroupId == groupId)
                    .OrderBy(s => s.StartTime)
                    .ToListAsync();

                await Send.Log($"群组 {groupId} 共有 {segments.Count} 个对话段");

                // 2. 检查向量化状态
                var vectorizedSegments = segments.Where(s => s.IsVectorized).ToList();
                await Send.Log($"其中 {vectorizedSegments.Count} 个已向量化");

                // 3. 检查向量索引记录
                var vectorIndexes = await DataContext.VectorIndexes
                    .Where(vi => vi.GroupId == groupId && vi.VectorType == "ConversationSegment")
                    .ToListAsync();

                await Send.Log($"向量索引记录数: {vectorIndexes.Count}");

                // 4. 检查FaissIndex分布
                var faissIndexes = vectorIndexes.Select(vi => vi.FaissIndex).OrderBy(x => x).ToList();
                await Send.Log($"FAISS索引分布: {string.Join(",", faissIndexes.Take(10))}...");

                // 5. 检查重复的FaissIndex
                var duplicates = faissIndexes.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
                if (duplicates.Any()) {
                    await Send.Log($"发现重复的FAISS索引: {string.Join(",", duplicates.Select(d => $"{d.Key}({d.Count()}次)"))}");
                } else {
                    await Send.Log("没有发现重复的FAISS索引");
                }

                // 6. 显示前几个对话段的内容摘要
                await Send.Log("前5个对话段的内容摘要:");
                foreach (var segment in segments.Take(5)) {
                    var topicLength = segment.TopicKeywords?.Length ?? 0;
                    var summaryLength = segment.ContentSummary?.Length ?? 0;
                    var summary = segment.TopicKeywords?.Substring(0, Math.Min(50, topicLength)) ??
                                 segment.ContentSummary?.Substring(0, Math.Min(50, summaryLength)) ?? "无摘要";
                    await Send.Log($"  段{segment.Id}: {summary}... (向量化:{segment.IsVectorized})");
                }

                // 7. 执行实际搜索
                var searchOption = new TelegramSearchBot.Model.SearchOption {
                    ChatId = groupId,
                    Search = searchQuery,
                    Skip = 0,
                    Take = 5
                };

                var searchResult = await _faissVectorService.Search(searchOption);
                await Send.Log($"搜索结果数量: {searchResult.Count}");

                foreach (var message in searchResult.Messages) {
                    await Send.Log($"  结果: {message.Content}");
                }

                // 8. 检查搜索查询向量
                try {
                    var queryVector = await _faissVectorService.GenerateVectorAsync(searchQuery);
                    await Send.Log($"查询向量维度: {queryVector?.Length ?? 0}");

                    if (queryVector != null && queryVector.Length > 0) {
                        var vectorSum = queryVector.Sum();
                        var vectorMagnitude = Math.Sqrt(queryVector.Select(x => ( double ) ( x * x )).Sum());
                        await Send.Log($"查询向量特征 - 和:{vectorSum:F3}, 模长:{vectorMagnitude:F3}");
                    }
                } catch (Exception ex) {
                    await Send.Log($"生成查询向量失败: {ex.Message}");
                }

            } catch (Exception ex) {
                _logger.LogError(ex, $"调试向量搜索失败");
                await Send.Log($"调试失败: {ex.Message}");
            }
        }

    }
}
