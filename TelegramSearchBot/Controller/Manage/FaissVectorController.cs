using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Vector;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.View;
using System.Collections.Generic;
using TelegramSearchBot.Interface.Controller;

namespace TelegramSearchBot.Controller.Manage {
    /// <summary>
    /// FAISS向量数据库管理控制器
    /// </summary>
    public class FaissVectorController : IOnUpdate
    {

        public List<Type> Dependencies => new List<Type>() { typeof(AdminController) };

        private readonly FaissVectorService _faissVectorService;
        private readonly ConversationSegmentationService _segmentationService;
        private readonly AdminService _adminService;
        private readonly GenericView _commonMessageView;
        private readonly DataDbContext _dataDbContext;

        public FaissVectorController(
            FaissVectorService faissVectorService,
            ConversationSegmentationService segmentationService,
            AdminService adminService,
            GenericView commonMessageView,
            DataDbContext dataDbContext)
        {
            _faissVectorService = faissVectorService;
            _segmentationService = segmentationService;
            _adminService = adminService;
            _commonMessageView = commonMessageView;
            _dataDbContext = dataDbContext;
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            if (p.Update.Message?.Text == null) return;

            var message = p.Update.Message;
            var text = message.Text.Trim();

            // 检查是否是管理员
            if (!await _adminService.IsNormalAdmin(message.From.Id))
            {
                return;
            }

            switch (text.ToLowerInvariant())
            {
                case "/faiss_status":
                case "/faiss状态":
                    await HandleFaissStatus(p);
                    break;
                    
                case "/faiss_rebuild":
                case "/faiss重建":
                    await HandleFaissRebuild(p);
                    break;
                    
                case "/faiss_health":
                case "/faiss健康检查":
                    await HandleFaissHealth(p);
                    break;
                    
                case "/faiss_stats":
                case "/faiss统计":
                    await HandleFaissStats(p);
                    break;
                    
                case "/faiss_cleanup":
                case "/faiss清理":
                    await HandleFaissCleanup(p);
                    break;
            }
        }

        /// <summary>
        /// 处理FAISS状态查询
        /// </summary>
        private async Task HandleFaissStatus(PipelineContext p)
        {
            try
            {
                var statusMessage = new StringBuilder();
                statusMessage.AppendLine("**FAISS向量数据库状态报告**");
                statusMessage.AppendLine();

                // 检查健康状态
                var isHealthy = await _faissVectorService.IsHealthyAsync();
                statusMessage.AppendLine($"🔍 **健康状态**: {(isHealthy ? "✅ 正常" : "❌ 异常")}");
                statusMessage.AppendLine();

                // 统计索引文件
                var indexFiles = await _dataDbContext.FaissIndexFiles
                    .Where(f => f.IsValid)
                    .ToListAsync();

                var conversationIndexes = indexFiles.Where(f => f.IndexType == "ConversationSegment").ToList();
                var messageIndexes = indexFiles.Where(f => f.IndexType == "Message").ToList();

                statusMessage.AppendLine($"📊 **索引文件统计**:");
                statusMessage.AppendLine($"   • 对话段索引: {conversationIndexes.Count} 个");
                statusMessage.AppendLine($"   • 单消息索引: {messageIndexes.Count} 个");
                statusMessage.AppendLine($"   • 总向量数量: {indexFiles.Sum(f => f.VectorCount)}");
                statusMessage.AppendLine();

                // 磁盘使用情况
                var totalSize = indexFiles.Sum(f => f.FileSize);
                statusMessage.AppendLine($"💾 **存储使用**: {FormatBytes(totalSize)}");

                // 索引目录信息
                var indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
                if (Directory.Exists(indexDirectory))
                {
                    var files = Directory.GetFiles(indexDirectory, "*.faiss");
                    statusMessage.AppendLine($"📁 **索引目录**: {files.Length} 个文件");
                }

                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText(statusMessage.ToString())
                    .Render();
            }
            catch (Exception ex)
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText($"❌ 获取FAISS状态失败: {ex.Message}")
                    .Render();
            }
        }

        /// <summary>
        /// 处理FAISS重建
        /// </summary>
        private async Task HandleFaissRebuild(PipelineContext p)
        {
            try
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText("🔄 开始重建FAISS向量索引...")
                    .Render();

                // 获取所有群组
                var groups = await _dataDbContext.ConversationSegments
                    .Select(s => s.GroupId)
                    .Distinct()
                    .ToListAsync();

                var totalSegments = 0;
                var successGroups = 0;

                foreach (var groupId in groups)
                {
                    try
                    {
                        // 重新分段
                        await _segmentationService.CreateSegmentsForGroupAsync(groupId);
                        
                        // 重新向量化
                        await _faissVectorService.VectorizeGroupSegments(groupId);
                        
                        var groupSegmentCount = await _dataDbContext.ConversationSegments
                            .CountAsync(s => s.GroupId == groupId);
                        
                        totalSegments += groupSegmentCount;
                        successGroups++;
                    }
                    catch (Exception ex)
                    {
                        await _commonMessageView
                            .WithChatId(p.Update.Message.Chat.Id)
                            .WithReplyTo(p.Update.Message.MessageId)
                            .WithText($"⚠️ 群组 {groupId} 重建失败: {ex.Message}")
                            .Render();
                    }
                }

                var resultMessage = new StringBuilder();
                resultMessage.AppendLine("✅ **FAISS向量索引重建完成**");
                resultMessage.AppendLine();
                resultMessage.AppendLine($"📊 成功重建群组: {successGroups}/{groups.Count}");
                resultMessage.AppendLine($"🗣️ 处理对话段: {totalSegments} 个");

                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText(resultMessage.ToString())
                    .Render();
            }
            catch (Exception ex)
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText($"❌ FAISS索引重建失败: {ex.Message}")
                    .Render();
            }
        }

        /// <summary>
        /// 处理FAISS健康检查
        /// </summary>
        private async Task HandleFaissHealth(PipelineContext p)
        {
            try
            {
                var isHealthy = await _faissVectorService.IsHealthyAsync();
                
                var healthMessage = isHealthy 
                    ? "✅ FAISS向量服务运行正常" 
                    : "❌ FAISS向量服务异常，请检查日志";

                if (isHealthy)
                {
                    // 额外检查索引目录
                    var indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
                    var directoryExists = Directory.Exists(indexDirectory);
                    healthMessage += directoryExists 
                        ? "\n📁 索引目录正常" 
                        : "\n⚠️ 索引目录不存在";
                }

                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText(healthMessage)
                    .Render();
            }
            catch (Exception ex)
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText($"❌ 健康检查失败: {ex.Message}")
                    .Render();
            }
        }

        /// <summary>
        /// 处理FAISS统计信息
        /// </summary>
        private async Task HandleFaissStats(PipelineContext p)
        {
            try
            {
                var statsMessage = new StringBuilder();
                statsMessage.AppendLine("**📊 FAISS向量数据库详细统计**");
                statsMessage.AppendLine();

                // 对话段统计
                var totalSegments = await _dataDbContext.ConversationSegments.CountAsync();
                var vectorizedSegments = await _dataDbContext.ConversationSegments
                    .CountAsync(s => s.IsVectorized);
                var pendingSegments = totalSegments - vectorizedSegments;

                statsMessage.AppendLine($"🗣️ **对话段统计**:");
                statsMessage.AppendLine($"   • 总对话段: {totalSegments}");
                statsMessage.AppendLine($"   • 已向量化: {vectorizedSegments}");
                statsMessage.AppendLine($"   • 待处理: {pendingSegments}");
                statsMessage.AppendLine();

                // 向量索引统计
                var vectorIndexes = await _dataDbContext.VectorIndexes
                    .GroupBy(vi => vi.VectorType)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToListAsync();

                statsMessage.AppendLine($"🔢 **向量索引统计**:");
                foreach (var index in vectorIndexes)
                {
                    statsMessage.AppendLine($"   • {index.Type}: {index.Count}");
                }
                statsMessage.AppendLine();

                // 群组分布
                var groupStats = await _dataDbContext.ConversationSegments
                    .GroupBy(s => s.GroupId)
                    .Select(g => new { GroupId = g.Key, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .Take(10)
                    .ToListAsync();

                statsMessage.AppendLine($"📈 **活跃群组 Top 10**:");
                foreach (var group in groupStats)
                {
                    statsMessage.AppendLine($"   • 群组 {group.GroupId}: {group.Count} 个对话段");
                }

                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText(statsMessage.ToString())
                    .Render();
            }
            catch (Exception ex)
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText($"❌ 获取统计信息失败: {ex.Message}")
                    .Render();
            }
        }

        /// <summary>
        /// 处理FAISS清理
        /// </summary>
        private async Task HandleFaissCleanup(PipelineContext p)
        {
            try
            {
                var cleanupMessage = new StringBuilder();
                cleanupMessage.AppendLine("🧹 **开始清理FAISS数据**");
                cleanupMessage.AppendLine();

                // 清理无效的索引文件记录
                var invalidIndexFiles = await _dataDbContext.FaissIndexFiles
                    .Where(f => !f.IsValid)
                    .ToListAsync();

                _dataDbContext.FaissIndexFiles.RemoveRange(invalidIndexFiles);

                // 清理孤立的向量索引记录
                var orphanVectorIndexes = await _dataDbContext.VectorIndexes
                    .Where(vi => !_dataDbContext.ConversationSegments.Any(cs => cs.Id == vi.EntityId && vi.VectorType == "ConversationSegment"))
                    .ToListAsync();

                _dataDbContext.VectorIndexes.RemoveRange(orphanVectorIndexes);

                await _dataDbContext.SaveChangesAsync();

                // 清理磁盘上的孤立文件
                var indexDirectory = Path.Combine(Env.WorkDir, "faiss_indexes");
                var cleanedFiles = 0;
                
                if (Directory.Exists(indexDirectory))
                {
                    var diskFiles = Directory.GetFiles(indexDirectory, "*.faiss");
                    var validFilePaths = await _dataDbContext.FaissIndexFiles
                        .Where(f => f.IsValid)
                        .Select(f => f.FilePath)
                        .ToListAsync();

                    foreach (var file in diskFiles)
                    {
                        if (!validFilePaths.Contains(file))
                        {
                            try
                            {
                                File.Delete(file);
                                cleanedFiles++;
                            }
                            catch
                            {
                                // 忽略删除失败的文件
                            }
                        }
                    }
                }

                cleanupMessage.AppendLine($"✅ **清理完成**");
                cleanupMessage.AppendLine($"   • 删除无效索引记录: {invalidIndexFiles.Count}");
                cleanupMessage.AppendLine($"   • 删除孤立向量记录: {orphanVectorIndexes.Count}");
                cleanupMessage.AppendLine($"   • 删除孤立文件: {cleanedFiles}");

                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText(cleanupMessage.ToString())
                    .Render();
            }
            catch (Exception ex)
            {
                await _commonMessageView
                    .WithChatId(p.Update.Message.Chat.Id)
                    .WithReplyTo(p.Update.Message.MessageId)
                    .WithText($"❌ 清理失败: {ex.Message}")
                    .Render();
            }
        }

        /// <summary>
        /// 格式化字节大小
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
} 
