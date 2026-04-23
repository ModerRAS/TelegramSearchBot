using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Telegram.Bot.Types.ReplyMarkups;
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
            var normalizedCommand = command?.Trim() ?? string.Empty;
            var isEntryCommand = normalizedCommand == "OCR设置" || normalizedCommand == "OCR配置";

            var currentState = await redis.GetStateAsync();
            if (isEntryCommand) {
                await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
                return (true, await BuildMainMenuMessageAsync());
            }

            if (string.IsNullOrEmpty(currentState)) {
                return (false, string.Empty);
            }

            if (normalizedCommand == "退出") {
                await redis.DeleteKeysAsync();
                return (true, "已退出OCR配置");
            }

            if (normalizedCommand == "返回") {
                return await HandleBackAsync(redis, currentState);
            }

            if (_stateHandlers.TryGetValue(currentState, out var handler)) {
                return await handler(redis, normalizedCommand);
            }

            return (false, string.Empty);
        }

        private async Task<(bool, string)> HandleMainMenuAsync(EditOCRConfRedisHelper redis, string command) {
            if (command == "切换OCR引擎" || command == "1") {
                await redis.SetStateAsync(OCRConfState.SelectingEngine.GetDescription());
                return (true, await BuildEngineSelectionMessageAsync());
            }

            if (command == "查看配置详情" || command == "2") {
                return await HandleViewingConfigAsync(redis, command);
            }

            return (true, await BuildMainMenuMessageAsync());
        }

        private async Task<(bool, string)> HandleSelectingEngineAsync(EditOCRConfRedisHelper redis, string command) {
            if (command == "1" || command == "PaddleOCR") {
                await SetEngineAsync(OCREngine.PaddleOCR);
                await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
                return (true, $"已切换到PaddleOCR引擎{System.Environment.NewLine}{System.Environment.NewLine}{await BuildMainMenuMessageAsync()}");
            } else if (command == "2" || command == "LLM") {
                await SetEngineAsync(OCREngine.LLM);
                await redis.SetStateAsync(OCRConfState.SelectingLLMChannel.GetDescription());
                return (true, await BuildLLMChannelSelectionMessageAsync());
            }

            return (true, $"无效选择，请使用下方键盘选择 OCR 引擎。{System.Environment.NewLine}{System.Environment.NewLine}{await BuildEngineSelectionMessageAsync()}");
        }

        private async Task<(bool, string)> HandleSelectingLLMChannelAsync(EditOCRConfRedisHelper redis, string command) {
            if (TryParseChannelId(command, out var channelId)) {
                var channel = await DataContext.LLMChannels
                    .FirstOrDefaultAsync(c => c.Id == channelId);

                if (channel != null) {
                    await redis.SetChannelIdAsync(channelId);
                    await redis.SetStateAsync(OCRConfState.SelectingLLMModel.GetDescription());
                    return (true, await BuildLLMModelSelectionMessageAsync(channelId, channel.Name));
                }
            }

            return (true, $"无效的渠道ID，请使用下方键盘重新选择。{System.Environment.NewLine}{System.Environment.NewLine}{await BuildLLMChannelSelectionMessageAsync()}");
        }

        private async Task<(bool, string)> HandleSelectingLLMModelAsync(EditOCRConfRedisHelper redis, string command) {
            var channelId = await redis.GetChannelIdAsync();
            if (!channelId.HasValue) {
                await redis.SetStateAsync(OCRConfState.SelectingLLMChannel.GetDescription());
                return (true, await BuildLLMChannelSelectionMessageAsync());
            }

            await SetLLMConfigAsync(channelId.Value, command);
            await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());

            return (true, $"OCR配置完成！{System.Environment.NewLine}引擎: LLM{System.Environment.NewLine}渠道ID: {channelId.Value}{System.Environment.NewLine}模型: {command}{System.Environment.NewLine}{System.Environment.NewLine}{await BuildMainMenuMessageAsync()}");
        }

        private async Task<(bool, string)> HandleViewingConfigAsync(EditOCRConfRedisHelper redis, string command) {
            await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
            return (true, await BuildCurrentConfigMessageAsync());
        }

        public async Task<ReplyMarkup> GetReplyMarkupAsync(long chatId) {
            var redis = new EditOCRConfRedisHelper(connectionMultiplexer, chatId);
            var currentState = await redis.GetStateAsync();
            if (string.IsNullOrEmpty(currentState)) {
                return new ReplyKeyboardRemove();
            }

            if (currentState == OCRConfState.MainMenu.GetDescription()) {
                return CreateReplyKeyboard(
                    new[] { "切换OCR引擎", "查看配置详情" },
                    new[] { "退出" });
            }

            if (currentState == OCRConfState.SelectingEngine.GetDescription()) {
                return CreateReplyKeyboard(
                    new[] { "PaddleOCR", "LLM" },
                    new[] { "返回", "退出" });
            }

            if (currentState == OCRConfState.SelectingLLMChannel.GetDescription()) {
                var channels = await GetAvailableLLMChannelsAsync();
                var rows = channels
                    .Select(channel => new[] { $"{channel.Id}. {channel.Name} ({channel.Provider})" })
                    .ToList();
                rows.Add(new[] { "返回", "退出" });
                return CreateReplyKeyboard(rows.ToArray());
            }

            if (currentState == OCRConfState.SelectingLLMModel.GetDescription()) {
                var channelId = await redis.GetChannelIdAsync();
                if (!channelId.HasValue) {
                    return CreateReplyKeyboard(new[] { "返回", "退出" });
                }

                var models = await GetAvailableModelsAsync(channelId.Value);
                var rows = models
                    .Select(model => new[] { model })
                    .ToList();
                rows.Add(new[] { "返回", "退出" });
                return CreateReplyKeyboard(rows.ToArray());
            }

            return new ReplyKeyboardRemove();
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

        private async Task<(bool, string)> HandleBackAsync(EditOCRConfRedisHelper redis, string currentState) {
            if (currentState == OCRConfState.MainMenu.GetDescription()) {
                await redis.DeleteKeysAsync();
                return (true, "已退出OCR配置");
            }

            if (currentState == OCRConfState.SelectingEngine.GetDescription() ||
                currentState == OCRConfState.ViewingConfig.GetDescription()) {
                await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
                return (true, await BuildMainMenuMessageAsync());
            }

            if (currentState == OCRConfState.SelectingLLMChannel.GetDescription()) {
                await redis.SetStateAsync(OCRConfState.SelectingEngine.GetDescription());
                return (true, await BuildEngineSelectionMessageAsync());
            }

            if (currentState == OCRConfState.SelectingLLMModel.GetDescription()) {
                await redis.SetStateAsync(OCRConfState.SelectingLLMChannel.GetDescription());
                return (true, await BuildLLMChannelSelectionMessageAsync());
            }

            await redis.SetStateAsync(OCRConfState.MainMenu.GetDescription());
            return (true, await BuildMainMenuMessageAsync());
        }

        private async Task<string> BuildMainMenuMessageAsync() {
            var sb = new StringBuilder();
            sb.AppendLine("🔧 OCR配置");
            sb.AppendLine();
            sb.AppendLine($"当前OCR引擎: {await GetCurrentEngineAsync()}");
            sb.AppendLine();
            sb.AppendLine("请使用下方键盘选择操作。");
            return sb.ToString();
        }

        private Task<string> BuildEngineSelectionMessageAsync() {
            var sb = new StringBuilder();
            sb.AppendLine("请选择 OCR 引擎：");
            sb.AppendLine("- PaddleOCR：本地 OCR");
            sb.AppendLine("- LLM：使用视觉模型读取图片文字");
            sb.AppendLine();
            sb.AppendLine("可使用下方键盘直接选择。");
            return Task.FromResult(sb.ToString());
        }

        private async Task<string> BuildLLMChannelSelectionMessageAsync() {
            var channels = await GetAvailableLLMChannelsAsync();
            if (!channels.Any()) {
                await SetEngineAsync(OCREngine.PaddleOCR);
                return $"当前没有可用的 LLM 渠道，已切回 PaddleOCR。{System.Environment.NewLine}{System.Environment.NewLine}{await BuildMainMenuMessageAsync()}";
            }

            var sb = new StringBuilder();
            sb.AppendLine("请选择 LLM 渠道：");
            foreach (var channel in channels) {
                sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
            }
            sb.AppendLine();
            sb.AppendLine("可使用下方键盘直接选择。");
            return sb.ToString();
        }

        private async Task<string> BuildLLMModelSelectionMessageAsync(int channelId, string channelName) {
            var models = await GetAvailableModelsAsync(channelId);
            var sb = new StringBuilder();
            sb.AppendLine($"已选择渠道: {channelName}");
            sb.AppendLine();
            sb.AppendLine("请选择 OCR 使用的模型：");
            if (models.Any()) {
                foreach (var model in models) {
                    sb.AppendLine($"- {model}");
                }
                sb.AppendLine();
                sb.AppendLine("可使用下方键盘直接选择。");
            } else {
                sb.AppendLine("该渠道下没有已配置模型，请直接发送模型名称。");
            }
            return sb.ToString();
        }

        private async Task<string> BuildCurrentConfigMessageAsync() {
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
            sb.AppendLine("请使用下方键盘继续操作。");
            return sb.ToString();
        }

        private async Task<List<string>> GetAvailableModelsAsync(int channelId) {
            return await DataContext.ChannelsWithModel
                .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                .Select(m => m.ModelName)
                .ToListAsync();
        }

        private static bool TryParseChannelId(string command, out int channelId) {
            if (int.TryParse(command, out channelId)) {
                return true;
            }

            var match = Regex.Match(command, @"^\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out channelId)) {
                return true;
            }

            channelId = default;
            return false;
        }

        private static ReplyKeyboardMarkup CreateReplyKeyboard(params string[][] rows) {
            return new ReplyKeyboardMarkup(rows
                .Select(row => row.Select(text => new KeyboardButton(text)).ToArray())
                .ToArray()) {
                ResizeKeyboard = true
            };
        }
    }
}
