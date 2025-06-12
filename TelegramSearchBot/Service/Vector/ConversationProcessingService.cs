using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.Vector
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class ConversationProcessingService : BackgroundService, IService
    {
        public string ServiceName => "ConversationProcessingService";

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ConversationProcessingService> _logger;
        private readonly TimeSpan _processingInterval = TimeSpan.FromMinutes(30); // 每30分钟检查一次

        public ConversationProcessingService(
            IServiceProvider serviceProvider,
            ILogger<ConversationProcessingService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("对话处理服务启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessConversations();
                    await Task.Delay(_processingInterval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "对话处理服务发生错误");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // 出错后等待5分钟再重试
                }
            }
        }

        private async Task ProcessConversations()
        {
            using var scope = _serviceProvider.CreateScope();
            var segmentationService = scope.ServiceProvider.GetRequiredService<ConversationSegmentationService>();
            var vectorService = scope.ServiceProvider.GetRequiredService<FaissVectorService>();

            try
            {
                _logger.LogInformation("开始处理对话段");

                // 1. 获取需要重新分段的群组
                var groupsNeedingSegmentation = await segmentationService.GetGroupsNeedingResegmentation();
                _logger.LogInformation($"发现 {groupsNeedingSegmentation.Count} 个群组需要重新分段");

                // 2. 为每个群组创建对话段
                foreach (var groupId in groupsNeedingSegmentation)
                {
                    try
                    {
                        // 获取该群组最后一个对话段的结束时间
                        using var dbScope = _serviceProvider.CreateScope();
                        var dbContext = dbScope.ServiceProvider.GetRequiredService<DataDbContext>();
                        
                        var lastSegment = await dbContext.ConversationSegments
                            .Where(s => s.GroupId == groupId)
                            .OrderByDescending(s => s.EndTime)
                            .FirstOrDefaultAsync();

                        var startTime = lastSegment?.EndTime ?? DateTime.UtcNow.AddDays(-7); // 如果没有历史段，从7天前开始
                        
                        await segmentationService.CreateSegmentsForGroupAsync(groupId, startTime);
                        _logger.LogInformation($"群组 {groupId} 分段完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"群组 {groupId} 分段失败");
                    }
                }

                // 3. 向量化未处理的对话段
                await VectorizeUnprocessedSegments(vectorService);

                _logger.LogInformation("对话段处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理对话段时发生错误");
            }
        }

        private async Task VectorizeUnprocessedSegments(FaissVectorService vectorService)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

            try
            {
                // 获取所有未向量化的对话段
                var unvectorizedSegments = await dbContext.ConversationSegments
                    .Where(s => !s.IsVectorized)
                    .Select(s => new { s.GroupId, s.Id })
                    .GroupBy(s => s.GroupId)
                    .ToListAsync();

                _logger.LogInformation($"发现 {unvectorizedSegments.Sum(g => g.Count())} 个未向量化的对话段");

                // 按群组处理
                foreach (var groupSegments in unvectorizedSegments)
                {
                    try
                    {
                        await vectorService.VectorizeGroupSegments(groupSegments.Key);
                        _logger.LogInformation($"群组 {groupSegments.Key} 向量化完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"群组 {groupSegments.Key} 向量化失败");
                    }
                }

                _logger.LogInformation("未向量化对话段处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向量化未处理对话段时发生错误");
            }
        }

        /// <summary>
        /// 手动触发处理（用于测试或管理员命令）
        /// </summary>
        public async Task TriggerProcessing()
        {
            _logger.LogInformation("手动触发对话处理");
            await ProcessConversations();
        }

        /// <summary>
        /// 为特定群组触发处理
        /// </summary>
        public async Task ProcessSpecificGroup(long groupId)
        {
            using var scope = _serviceProvider.CreateScope();
            var segmentationService = scope.ServiceProvider.GetRequiredService<ConversationSegmentationService>();
            var vectorService = scope.ServiceProvider.GetRequiredService<FaissVectorService>();

            try
            {
                _logger.LogInformation($"开始处理群组 {groupId}");
                
                // 分段
                await segmentationService.CreateSegmentsForGroupAsync(groupId);
                
                // 向量化
                await vectorService.VectorizeGroupSegments(groupId);
                
                _logger.LogInformation($"群组 {groupId} 处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理群组 {groupId} 时发生错误");
                throw;
            }
        }
    }
} 