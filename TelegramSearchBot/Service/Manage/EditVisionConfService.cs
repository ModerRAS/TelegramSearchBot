using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class EditVisionConfService : IService {
        public string ServiceName => "EditVisionConfService";
        protected readonly DataDbContext DataContext;
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }

        private readonly Dictionary<string, Func<EditVisionConfRedisHelper, string, Task<(bool, string)>>> _stateHandlers;

        public EditVisionConfService(
            DataDbContext context,
            IConnectionMultiplexer connectionMultiplexer
            ) {
            this.connectionMultiplexer = connectionMultiplexer;
            DataContext = context;

            _stateHandlers = new Dictionary<string, Func<EditVisionConfRedisHelper, string, Task<(bool, string)>>>
            {
                { VisionConfState.SelectChannel.GetDescription(), HandleSelectChannelAsync },
                { VisionConfState.SelectModel.GetDescription(), HandleSelectModelAsync },
                { VisionConfState.ToggleVision.GetDescription(), HandleToggleVisionAsync },
                { VisionConfState.ViewChannel.GetDescription(), HandleViewChannelAsync }
            };
        }

        public async Task<(bool, string)> ExecuteAsync(string Command, long ChatId) {
            var redis = new EditVisionConfRedisHelper(connectionMultiplexer, ChatId);
            var currentState = await redis.GetStateAsync();

            // 处理直接命令
            var directResult = await HandleDirectCommandsAsync(redis, Command);
            if (directResult.HasValue) {
                return directResult.Value;
            }

            // 处理当前状态的输入
            if (string.IsNullOrEmpty(currentState)) {
                return (false, "");
            }

            if (_stateHandlers.TryGetValue(currentState, out var handler)) {
                return await handler(redis, Command);
            }

            return (false, "");
        }

        private async Task<(bool, string)?> HandleDirectCommandsAsync(EditVisionConfRedisHelper redis, string command) {
            var cmd = command.Trim();

            if (cmd.Equals("设置视觉", StringComparison.OrdinalIgnoreCase)) {
                return await ShowChannelListForVision(redis, VisionConfState.SelectChannel);
            }

            if (cmd.Equals("查看视觉", StringComparison.OrdinalIgnoreCase)) {
                return await ShowChannelListForVision(redis, VisionConfState.ViewChannel);
            }

            return null;
        }

        private async Task<(bool, string)> ShowChannelListForVision(EditVisionConfRedisHelper redis, VisionConfState nextState) {
            var channels = await DataContext.LLMChannels.ToListAsync();
            if (channels.Count == 0) {
                return (true, "当前没有可操作的渠道");
            }

            var sb = new StringBuilder();
            sb.AppendLine("请选择渠道ID：");
            foreach (var channel in channels) {
                sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
            }

            await redis.SetStateAsync(nextState.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleSelectChannelAsync(EditVisionConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var channelId)) {
                return (false, "请输入有效的渠道ID");
            }

            var channel = await DataContext.LLMChannels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null) {
                return (false, "找不到指定的渠道");
            }

            // 获取该渠道下的所有模型及其视觉能力状态
            var models = await DataContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                .ToListAsync();

            if (models.Count == 0) {
                await redis.DeleteKeysAsync();
                return (true, "该渠道下没有模型");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"渠道 {channel.Name} 下的模型列表：");
            for (int i = 0; i < models.Count; i++) {
                var model = models[i];
                var hasVision = model.Capabilities?.Any(c =>
                    c.CapabilityName == "vision" && c.CapabilityValue == "true") ?? false;
                var visionIcon = hasVision ? "👁️" : "❌";
                sb.AppendLine($"{i + 1}. {model.ModelName} {visionIcon}");
            }
            sb.AppendLine();
            sb.AppendLine("请输入模型序号来切换视觉支持状态：");

            await redis.SetDataAsync(channelId.ToString());
            await redis.SetStateAsync(VisionConfState.SelectModel.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleSelectModelAsync(EditVisionConfRedisHelper redis, string command) {
            var channelId = int.Parse(await redis.GetDataAsync());

            var models = await DataContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                .ToListAsync();

            if (!int.TryParse(command, out var modelIndex) || modelIndex < 1 || modelIndex > models.Count) {
                return (false, "请输入有效的模型序号");
            }

            var selectedModel = models[modelIndex - 1];
            var currentVision = selectedModel.Capabilities?.Any(c =>
                c.CapabilityName == "vision" && c.CapabilityValue == "true") ?? false;

            await redis.SetDataAsync($"{channelId}|{selectedModel.Id}");
            await redis.SetStateAsync(VisionConfState.ToggleVision.GetDescription());

            return (true, $"模型 {selectedModel.ModelName} 当前视觉状态: {(currentVision ? "✅ 已启用" : "❌ 未启用")}\n请输入 开启 或 关闭 来切换视觉支持：");
        }

        private async Task<(bool, string)> HandleToggleVisionAsync(EditVisionConfRedisHelper redis, string command) {
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            var channelWithModelId = int.Parse(parts[1]);

            var channelWithModel = await DataContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .FirstOrDefaultAsync(c => c.Id == channelWithModelId);

            if (channelWithModel == null) {
                await redis.DeleteKeysAsync();
                return (true, "找不到指定的模型");
            }

            bool enableVision;
            var cmd = command.Trim();
            if (cmd.Equals("开启", StringComparison.OrdinalIgnoreCase) || cmd.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("是", StringComparison.OrdinalIgnoreCase) || cmd.Equals("1", StringComparison.OrdinalIgnoreCase)) {
                enableVision = true;
            } else if (cmd.Equals("关闭", StringComparison.OrdinalIgnoreCase) || cmd.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                       cmd.Equals("否", StringComparison.OrdinalIgnoreCase) || cmd.Equals("0", StringComparison.OrdinalIgnoreCase)) {
                enableVision = false;
            } else {
                return (false, "请输入 开启 或 关闭");
            }

            // 更新或创建视觉能力
            var visionCap = channelWithModel.Capabilities?.FirstOrDefault(c => c.CapabilityName == "vision");
            if (visionCap != null) {
                visionCap.CapabilityValue = enableVision.ToString().ToLower();
                visionCap.LastUpdated = DateTime.UtcNow;
            } else {
                var newCap = new ModelCapability {
                    ChannelWithModelId = channelWithModelId,
                    CapabilityName = "vision",
                    CapabilityValue = enableVision.ToString().ToLower(),
                    Description = "手动设置的视觉能力",
                    LastUpdated = DateTime.UtcNow
                };
                DataContext.ModelCapabilities.Add(newCap);
            }

            // 同时更新 multimodal 和 image_content 能力
            await UpdateRelatedCapability(channelWithModel, "multimodal", enableVision);
            await UpdateRelatedCapability(channelWithModel, "image_content", enableVision);

            await DataContext.SaveChangesAsync();
            await redis.DeleteKeysAsync();

            var statusText = enableVision ? "✅ 已启用" : "❌ 已关闭";
            return (true, $"模型 {channelWithModel.ModelName} 的视觉支持已设置为: {statusText}");
        }

        private Task UpdateRelatedCapability(ChannelWithModel channelWithModel, string capName, bool value) {
            var cap = channelWithModel.Capabilities?.FirstOrDefault(c => c.CapabilityName == capName);
            if (cap != null) {
                cap.CapabilityValue = value.ToString().ToLower();
                cap.LastUpdated = DateTime.UtcNow;
            } else {
                var newCap = new ModelCapability {
                    ChannelWithModelId = channelWithModel.Id,
                    CapabilityName = capName,
                    CapabilityValue = value.ToString().ToLower(),
                    Description = "手动设置",
                    LastUpdated = DateTime.UtcNow
                };
                DataContext.ModelCapabilities.Add(newCap);
            }
            return Task.CompletedTask;
        }

        private async Task<(bool, string)> HandleViewChannelAsync(EditVisionConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var channelId)) {
                return (false, "请输入有效的渠道ID");
            }

            var channel = await DataContext.LLMChannels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null) {
                await redis.DeleteKeysAsync();
                return (true, "找不到指定的渠道");
            }

            var models = await DataContext.ChannelsWithModel
                .Include(c => c.Capabilities)
                .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"渠道 {channel.Name} ({channel.Provider}) 的模型视觉支持状态：");

            if (models.Count == 0) {
                sb.AppendLine("暂无模型");
            } else {
                foreach (var model in models) {
                    var hasVision = model.Capabilities?.Any(c =>
                        c.CapabilityName == "vision" && c.CapabilityValue == "true") ?? false;
                    var hasMultimodal = model.Capabilities?.Any(c =>
                        c.CapabilityName == "multimodal" && c.CapabilityValue == "true") ?? false;
                    var hasToolCalling = model.Capabilities?.Any(c =>
                        c.CapabilityName == "function_calling" && c.CapabilityValue == "true") ?? false;

                    var visionIcon = hasVision ? "👁️" : "❌";
                    var toolIcon = hasToolCalling ? "🔧" : "";

                    sb.AppendLine($"- {model.ModelName} 视觉:{visionIcon} {toolIcon}");
                }
            }

            await redis.DeleteKeysAsync();
            return (true, sb.ToString());
        }
    }
}
