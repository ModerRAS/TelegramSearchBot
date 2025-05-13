using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interfaces;

namespace TelegramSearchBot.Service.Common
{
    /// <summary>
    /// Orleans流监听器，负责初始化和管理流订阅
    /// </summary>
    public class OrleansStreamListener
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger<OrleansStreamListener> _logger;

        public OrleansStreamListener(IGrainFactory grainFactory, ILogger<OrleansStreamListener> logger)
        {
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 初始化流监听器
        /// </summary>
        public void Initialize()
        {
            _logger.LogInformation("正在初始化Orleans流监听器...");
            
            try
            {
                // 创建关键的Grain实例以确保它们订阅相应的流
                // 这些Grain的OnActivateAsync方法会自动订阅相应的流
                
                // 音频识别Grain
                var asrGrain = _grainFactory.GetGrain<IAsrGrain>(Guid.NewGuid());
                
                // B站链接处理Grain
                var bilibiliGrain = _grainFactory.GetGrain<IBilibiliLinkProcessingGrain>(Guid.NewGuid());
                
                // URL提取Grain
                var urlExtractionGrain = _grainFactory.GetGrain<IUrlExtractionGrain>(Guid.NewGuid());

                _logger.LogInformation("Orleans流监听器初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Orleans流监听器初始化失败");
            }
        }
    }
} 