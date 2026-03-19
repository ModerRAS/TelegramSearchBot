using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.Manage;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class EditOCRConfService : IService {
        public string ServiceName => "EditOCRConfService";
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<EditOCRConfService> _logger;
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }

        public const string OCREngineKey = "OCR:Engine";
        public const string OCRLLMModelNameKey = "OCR:LLMModelName";
        public const string OCRLLMChannelIdKey = "OCR:LLMChannelId";

        private readonly Dictionary<string, Func<EditOCRConfRedisHelper, string, Task<(bool, string)>>> _stateHandlers;

        public EditOCRConfService(
            DataDbContext context,
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<EditOCRConfService> logger
            ) {
            this.connectionMultiplexer = connectionMultiplexer;
            DataContext = context;
            _logger = logger;

            _stateHandlers = new Dictionary<string, Func<EditOCRConfRedisHelper, string, Task<(bool, string)>>>
            {
                { OCRConfState.MainMenu.GetDescription(), HandleMainMenuAsync },
                { OCRConfState.SelectingEngine.GetDescription(), HandleSelectingEngineAsync },
                { OCRConfState.SelectingLLMChannel.GetDescription(), HandleSelectingLLMChannelAsync },
                { OCRConfState.SelectingLLMModel.GetDescription(), HandleSelectingLLMModelAsync },
                { OCRConfState.ViewingConfig.GetDescription(), HandleViewingConfigAsync }
            };
        }

        public async Task<(bool, string)> ExecuteAsync(string command, long chatId) {
            var redis = new EditOCRConfRedisHelper(connectionMultiplexer, chatId);
            
            var currentState = await redis.GetStateAsync();
            if (string.IsNullOrEmpty(currentState)) {
                currentState = OCRConfState.MainMenu.GetDescription();
                await redis.SetStateAsync(currentState);
            }

            if (command == "退出" || command == "返回") {
                await redis.DeleteKeysAsync();
                return (true, "已退出OCR配置");
            }

            if (command == "OCR设置" || command == "OCR配置") {
                currentState = OCRConfState.MainMenu.GetDescription();
                await redis.SetStateAsync(currentState);
            }

            if (_stateHandlers.TryGetValue(currentState, out var handler)) {
                return await handler(redis, command);
            }

            return (false, string.Empty);
        }

        private async Task<(bool, string)> HandleMainMenuAsync(EditOCRConfRedisHelper redis, string command) {
            var sb = new StringBuilder();
            sb.AppendLine("🔧 OCR配置");
            sb.AppendLine();
            
            var currentEngine = await GetCurrentEngineAsync();
            sb.AppendLine($"当前OCR引擎: {currentEngine}");
            sb.AppendLine();
            sb.AppendLine("请选择操作：");
            sb.AppendLine("1. 切换OCR引擎");
            sb.AppendLine("2. 查看配置详情");
            sb.AppendLine("3. 返回");

            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleSelectingEngineAsync(EditOCRConfRedisHelper redis, string command) {
            if (command == "1" || command == "PaddleOCR") {
                await SetEngineAsync(OCREngine.PaddleOCR);
                await redis.DeleteKeysAsync();
                return (true, "已切换到PaddleOCR引擎");
            } else if (command == "2" || command == "LLM") {
                await SetEngineAsync(OCREngine.LLM);
                await redis.SetStateAsync(OCRConfState.SelectingLLMChannel.GetDescription());
                
                var channels = await GetAvailableLLMChannelsAsync();
                var sb = new StringBuilder();
                sb.AppendLine("已切换到LLM引擎");
                sb.AppendLine();
                sb.AppendLine("请选择LLM渠道（输入渠道ID）：");
                foreach (var channel in channels) {
                    sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                }
                return (true, sb.ToString());
            }

            return (true, "无效选择，请输入1或2");
        }

        private async Task<(bool, string)> HandleSelectingLLMChannelAsync(EditOCRConfRedisHelper redis, string command) {
            if (int.TryParse(command, out var channelId)) {
                var channel = await DataContext.LLMChannels
                    .FirstOrDefaultAsync(c => c.Id == channelId);
                
                if (channel != null) {
                    await redis.SetChannelIdAsync(channelId);
                    await redis.SetStateAsync(OCRConfState.SelectingLLMModel.GetDescription());
                    
                    var models = await DataContext.ChannelsWithModel
                        .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                        .Select(m => m.ModelName)
                        .ToListAsync();
                    
                    var sb = new StringBuilder();
                    sb.AppendLine($"已选择渠道: {channel.Name}");
                    sb.AppendLine();
                    sb.AppendLine("请输入OCR使用的模型名称：");
                    if (models.Any()) {
                        sb.AppendLine("可选模型：");
                        foreach (var model in models) {
                            sb.AppendLine($"- {model}");
                        }
                    }
                    return (true, sb.ToString());
                }
            }

            return (true, "无效的渠道ID，请重新输入");
        }

        private async Task<(bool, string)> HandleSelectingLLMModelAsync(EditOCRConfRedisHelper redis, string command) {
            var channelId = await redis.GetChannelIdAsync();
            if (!channelId.HasValue) {
                await redis.SetStateAsync(OCRConfState.SelectingLLMChannel.GetDescription());
                return (true, "请先选择LLM渠道");
            }

            await SetLLMConfigAsync(channelId.Value, command);
            await redis.DeleteKeysAsync();
            
            return (true, $"OCR配置完成！\n引擎: LLM\n渠道ID: {channelId}\n模型: {command}");
        }

        private async Task<(bool, string)> HandleViewingConfigAsync(EditOCRConfRedisHelper redis, string command) {
            var sb = new StringBuilder();
            sb.AppendLine("📋 OCR配置详情");
            sb.AppendLine();
            sb.AppendLine($"引擎: {await GetCurrentEngineAsync()}");
            
            var engineConfig = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == OCREngineKey);
            
            if (engineConfig?.Value == OCREngine.LLM.ToString()) {
                var channelConfig = await DataContext.AppConfigurationItems
                    .FirstOrDefaultAsync(x => x.Key == OCRLLMChannelIdKey);
                var modelConfig = await DataContext.AppConfigurationItems
                    .FirstOrDefaultAsync(x => x.Key == OCRLLMModelNameKey);
                
                if (channelConfig != null && int.TryParse(channelConfig.Value, out var channelId)) {
                    var channel = await DataContext.LLMChannels
                        .FirstOrDefaultAsync(c => c.Id == channelId);
                    sb.AppendLine($"LLM渠道: {channel?.Name ?? "未找到"} ({channelId})");
                }
                
                if (modelConfig != null) {
                    sb.AppendLine($"LLM模型: {modelConfig.Value}");
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("输入\"返回\"返回主菜单");

            await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<string> GetCurrentEngineAsync() {
            var config = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == OCREngineKey);
            return config?.Value ?? OCREngine.PaddleOCR.ToString();
        }

        private async Task SetEngineAsync(OCREngine engine) {
            var config = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == OCREngineKey);
            
            if (config == null) {
                await DataContext.AppConfigurationItems.AddAsync(new AppConfigurationItem {
                    Key = OCREngineKey,
                    Value = engine.ToString()
                });
            } else {
                config.Value = engine.ToString();
            }
            
            await DataContext.SaveChangesAsync();
        }

        private async Task SetLLMConfigAsync(int channelId, string modelName) {
            await SetConfigAsync(OCRLLMChannelIdKey, channelId.ToString());
            await SetConfigAsync(OCRLLMModelNameKey, modelName);
        }

        private async Task SetConfigAsync(string key, string value) {
            var config = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (config == null) {
                await DataContext.AppConfigurationItems.AddAsync(new AppConfigurationItem {
                    Key = key,
                    Value = value
                });
            } else {
                config.Value = value;
            }
            
            await DataContext.SaveChangesAsync();
        }

        private async Task<List<LLMChannel>> GetAvailableLLMChannelsAsync() {
            return await DataContext.LLMChannels.ToListAsync();
        }
    }
}
