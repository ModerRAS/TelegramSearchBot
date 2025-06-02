using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.AI
{
    /// <summary>
    /// LLM渠道配置DTO - 用于LLM核心适配器层
    /// </summary>
    public class LLMChannelDto
    {
        /// <summary>
        /// 渠道ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 渠道名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// API网关地址
        /// </summary>
        public string Gateway { get; set; }

        /// <summary>
        /// API密钥
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// LLM提供商
        /// </summary>
        public LLMProvider Provider { get; set; }

        /// <summary>
        /// 最大并发数
        /// </summary>
        public int MaxConcurrency { get; set; } = 1;

        /// <summary>
        /// 优先级（数字越大优先级越高）
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 超时设置（秒）
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 每分钟最大请求数（速率限制）
        /// </summary>
        public int MaxRequestsPerMinute { get; set; } = 60;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 健康状态
        /// </summary>
        public bool IsHealthy { get; set; } = true;

        /// <summary>
        /// 最后健康检查时间
        /// </summary>
        public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 扩展配置
        /// </summary>
        public Dictionary<string, object> ExtendedConfig { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 检查渠道是否可用
        /// </summary>
        public bool IsAvailable => IsEnabled && IsHealthy && !string.IsNullOrWhiteSpace(Gateway);

        /// <summary>
        /// 从数据库LLMChannel转换为DTO
        /// </summary>
        public static LLMChannelDto FromDataModel(TelegramSearchBot.Model.Data.LLMChannel dataChannel)
        {
            if (dataChannel == null) return null;

            return new LLMChannelDto
            {
                Id = dataChannel.Id,
                Name = dataChannel.Name,
                Gateway = dataChannel.Gateway,
                ApiKey = dataChannel.ApiKey,
                Provider = dataChannel.Provider,
                MaxConcurrency = dataChannel.Parallel,
                Priority = dataChannel.Priority,
                IsEnabled = true,
                IsHealthy = true,
                TimeoutSeconds = 60,
                MaxRequestsPerMinute = 60
            };
        }

        /// <summary>
        /// 转换为数据库LLMChannel
        /// </summary>
        public TelegramSearchBot.Model.Data.LLMChannel ToDataModel()
        {
            return new TelegramSearchBot.Model.Data.LLMChannel
            {
                Id = Id,
                Name = Name,
                Gateway = Gateway,
                ApiKey = ApiKey,
                Provider = Provider,
                Parallel = MaxConcurrency,
                Priority = Priority
            };
        }
    }
} 