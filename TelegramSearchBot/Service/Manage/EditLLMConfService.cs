using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.Manage;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class EditLLMConfService : IService {
        public string ServiceName => "EditLLMConfService";
        protected readonly DataDbContext DataContext;
        protected readonly IEditLLMConfHelper Helper;
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        public EditLLMConfService(
            IEditLLMConfHelper helper,
            DataDbContext context,
            IConnectionMultiplexer connectionMultiplexer
            ) {
            this.connectionMultiplexer = connectionMultiplexer;
            DataContext = context;
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }
        private async Task<(bool, string)> HandleAwaitingNameAsync(EditLLMConfRedisHelper redis, string command) {
            await redis.SetDataAsync(command); // 存储名称
            await redis.SetStateAsync(LLMConfState.AwaitingGateway.GetDescription());
            return (true, "请输入渠道地址");
        }

        private async Task<(bool, string)> HandleAwaitingGatewayAsync(EditLLMConfRedisHelper redis, string command) {
            var name = await redis.GetDataAsync();
            await redis.SetDataAsync($"{name}|{command}"); // 追加地址
            await redis.SetStateAsync(LLMConfState.AwaitingProvider.GetDescription());

            // 动态生成LLMProvider枚举选项(编辑模式)
            var editProviderOptions = new StringBuilder();
            var editProviderType = typeof(LLMProvider);
            var editProviders = Enum.GetValues(editProviderType)
                .Cast<LLMProvider>()
                .Where(p => p != LLMProvider.None)
                .Select((p, i) => $"{i + 1}. {p}");

            editProviderOptions.AppendLine("请选择渠道类型：");
            editProviderOptions.AppendJoin("\n", editProviders);
            return (true, editProviderOptions.ToString());
        }

        private async Task<(bool, string)> HandleAwaitingProviderAsync(EditLLMConfRedisHelper redis, string command) {
            var nameAndGateway = ( await redis.GetDataAsync() ).Split('|');
            LLMProvider provider;
            var validProviders = Enum.GetValues(typeof(LLMProvider))
                .Cast<LLMProvider>()
                .Where(p => p != LLMProvider.None)
                .ToList();

            if (int.TryParse(command.Trim(), out int providerIndex) &&
                providerIndex > 0 && providerIndex <= validProviders.Count) {
                provider = validProviders[providerIndex - 1];
            } else {
                return (false, $"无效的类型选择，请输入1到{validProviders.Count}之间的数字");
            }
            await redis.SetDataAsync($"{nameAndGateway[0]}|{nameAndGateway[1]}|{provider}");
            await redis.SetStateAsync(LLMConfState.AwaitingParallel.GetDescription());
            return (true, "请输入渠道的最大并行数量(默认1):");
        }

        private async Task<(bool, string)> HandleAwaitingParallelAsync(EditLLMConfRedisHelper redis, string command) {
            int parallel = string.IsNullOrEmpty(command) ? 1 : int.Parse(command);
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            await redis.SetDataAsync($"{parts[0]}|{parts[1]}|{parts[2]}|{parallel}");
            await redis.SetStateAsync(LLMConfState.AwaitingPriority.GetDescription());
            return (true, "请输入渠道的优先级(默认0):");
        }

        private async Task<(bool, string)> HandleAwaitingPriorityAsync(EditLLMConfRedisHelper redis, string command) {
            int priority = string.IsNullOrEmpty(command) ? 0 : int.Parse(command);
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            await redis.SetDataAsync($"{parts[0]}|{parts[1]}|{parts[2]}|{parts[3]}|{priority}");
            await redis.SetStateAsync(LLMConfState.AwaitingApiKey.GetDescription());
            return (true, "请输入渠道的API Key");
        }

        private async Task<(bool, string)> HandleAwaitingApiKeyAsync(EditLLMConfRedisHelper redis, string command) {
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            var apiKey = command;
            var provider = ( LLMProvider ) Enum.Parse(typeof(LLMProvider), parts[2]);

            // 创建渠道
            var result = await Helper.AddChannel(
                parts[0],
                parts[1],
                apiKey,
                provider,
                int.Parse(parts[3]),
                int.Parse(parts[4]));

            // 清理状态
            await redis.DeleteKeysAsync();

            return (true, result > 0 ? "渠道创建成功" : "渠道创建失败");
        }

        private async Task<(bool, string)> HandleSettingAltPhotoModelAsync(EditLLMConfRedisHelper redis, string command) {
            try {
                var config = await DataContext.AppConfigurationItems
                    .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.AltPhotoModelName);

                if (config == null) {
                    await DataContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
                        Key = GeneralLLMService.AltPhotoModelName,
                        Value = command
                    });
                } else {
                    config.Value = command;
                }

                await DataContext.SaveChangesAsync();
                await redis.DeleteKeysAsync();
                return (true, $"图片分析模型已设置为: {command}");
            } catch {
                return (false, "设置图片分析模型失败");
            }
        }

        private async Task<(bool, string)> HandleEditingSelectChannelAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var channelId)) {
                return (false, "请输入有效的渠道ID");
            }

            var channel = await Helper.GetChannelById(channelId);
            if (channel == null) {
                return (false, "找不到指定的渠道");
            }

            await redis.SetDataAsync(channelId.ToString());
            await redis.SetStateAsync(LLMConfState.EditingSelectField.GetDescription());
            return (true, $"请选择要编辑的字段：\n1. 名称 ({channel.Name})\n2. 地址 ({channel.Gateway})\n3. 类型 ({channel.Provider})\n4. API Key\n5. 最大并行数量 ({channel.Parallel})\n6. 优先级 ({channel.Priority})");
        }

        private async Task<(bool, string)> HandleEditingSelectFieldAsync(EditLLMConfRedisHelper redis, string command) {
            var value = await redis.GetDataAsync();
            var editChannelId = int.Parse(value);
            await redis.SetDataAsync($"{editChannelId}|{command}");

            if (command == "3") {
                await redis.SetStateAsync(LLMConfState.EditingInputValue.GetDescription());

                // 动态生成LLMProvider枚举选项
                var providerOptions = new StringBuilder();
                var providerType = typeof(LLMProvider);
                var providers = Enum.GetValues(providerType)
                    .Cast<LLMProvider>()
                    .Where(p => p != LLMProvider.None)
                    .Select((p, i) => $"{i + 1}. {p}");

                providerOptions.AppendLine("请选择渠道类型：");
                providerOptions.AppendJoin("\n", providers);
                return (true, providerOptions.ToString());
            } else {
                await redis.SetStateAsync(LLMConfState.EditingInputValue.GetDescription());
                return (true, "请输入新的值：");
            }
        }

        private async Task<(bool, string)> HandleAddingModelSelectChannelAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var addModelChannelId)) {
                return (false, "请输入有效的渠道ID");
            }

            var addModelChannel = await Helper.GetChannelById(addModelChannelId);
            if (addModelChannel == null) {
                return (false, "找不到指定的渠道");
            }

            await redis.SetDataAsync(addModelChannelId.ToString());
            await redis.SetStateAsync(LLMConfState.AddingModelInput.GetDescription());
            return (true, "请输入要添加的模型名称，多个模型用逗号或分号分隔");
        }

        private async Task<(bool, string)> HandleAddingModelInputAsync(EditLLMConfRedisHelper redis, string command) {
            var addChannelId = int.Parse(await redis.GetDataAsync());
            var ModelResult = await Helper.AddModelWithChannel(addChannelId, command);

            // 清理状态
            await redis.DeleteKeysAsync();

            return (true, ModelResult ? "模型添加成功" : "模型添加失败");
        }

        private async Task<(bool, string)> HandleRemovingModelSelectChannelAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var removeModelChannelId)) {
                return (true, "请输入有效的渠道ID");
            }

            var removeModelChannel = await Helper.GetChannelById(removeModelChannelId);
            if (removeModelChannel == null) {
                return (true, "找不到指定的渠道");
            }

            // 获取该渠道下的所有模型
            var models = await DataContext.ChannelsWithModel
                .Where(m => m.LLMChannelId == removeModelChannelId)
                .Select(m => m.ModelName)
                .ToListAsync();

            if (models.Count == 0) {
                return (true, "该渠道下没有可移除的模型");
            }

            var sb = new StringBuilder();
            sb.AppendLine("请选择要移除的模型：");
            for (int i = 0; i < models.Count; i++) {
                sb.AppendLine($"{i + 1}. {models[i]}");
            }

            await redis.SetDataAsync($"{removeModelChannelId}|{string.Join(",", models)}");
            await redis.SetStateAsync(LLMConfState.RemovingModelSelect.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleRemovingModelSelectAsync(EditLLMConfRedisHelper redis, string command) {
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');

            if (parts.Length < 2)
            {
                return (false, "内部错误：模型数据格式不正确");
            }

            var removeChannelId = int.Parse(parts[0]);
            var modelList = parts[1].Split(',');

            if (modelList.Length == 0 || (modelList.Length == 1 && string.IsNullOrEmpty(modelList[0])))
            {
                 return (true, "该渠道下没有可移除的模型");
            }

            if (!int.TryParse(command, out var modelIndex) || modelIndex < 1 || modelIndex > modelList.Length) {
                return (true, "请输入有效的模型序号");
            }

            if (modelIndex - 1 < 0 || modelIndex - 1 >= modelList.Length)
            {
                 return (true, "内部错误：无效的模型序号");
            }

            var modelName = modelList[modelIndex - 1];
            var removeResult = await Helper.RemoveModelFromChannel(removeChannelId, modelName);

            // 清理状态
            await redis.DeleteKeysAsync();

            return (true, removeResult ? "模型移除成功" : "模型移除失败");
        }

        private async Task<(bool, string)> HandleViewingModelSelectChannelAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var viewModelChannelId)) {
                return (true, "请输入有效的渠道ID");
            }

            var viewModelChannel = await Helper.GetChannelById(viewModelChannelId);
            if (viewModelChannel == null) {
                return (true, "找不到指定的渠道");
            }

            // 获取该渠道下的所有模型
            var channelModels = await DataContext.ChannelsWithModel
                .Where(m => m.LLMChannelId == viewModelChannelId)
                .Select(m => m.ModelName)
                .ToListAsync();

            var modelSb = new StringBuilder();
            modelSb.AppendLine($"渠道 {viewModelChannel.Name} 下的模型列表：");
            if (channelModels.Count == 0) {
                modelSb.AppendLine("暂无模型");
            } else {
                foreach (var model in channelModels) {
                    modelSb.AppendLine($"- {model}");
                }
            }

            // 清理状态
            await redis.DeleteKeysAsync();

            return (true, modelSb.ToString());
        }

        private async Task<(bool, string)> HandleEditingInputValueAsync(EditLLMConfRedisHelper redis, string command) {
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            var editId = int.Parse(parts[0]);
            var field = parts[1];

            bool updateResult = false;
            switch (field) {
                case "1":
                    updateResult = await Helper.UpdateChannel(editId, name: command);
                    break;
                case "2":
                    updateResult = await Helper.UpdateChannel(editId, gateway: command);
                    break;
                case "3":
                    if (!Enum.TryParse<LLMProvider>(command, out var newProvider)) {
                        return (true, "无效的类型");
                    }
                    updateResult = await Helper.UpdateChannel(editId, provider: newProvider);
                    break;
                case "4":
                    updateResult = await Helper.UpdateChannel(editId, apiKey: command);
                    break;
                case "5":
                    if (!int.TryParse(command, out var tmp_parallel)) {
                        return (true, "请输入有效的数字");
                    }
                    updateResult = await Helper.UpdateChannel(editId, parallel: tmp_parallel);
                    break;
                case "6":
                    if (!int.TryParse(command, out var tmp_priority)) {
                        return (true, "请输入有效的数字");
                    }
                    updateResult = await Helper.UpdateChannel(editId, priority: tmp_priority);
                    break;
                default:
                    return (true, "无效的字段选择");
            }

            // 清理状态
            await redis.DeleteKeysAsync();

            return (true, updateResult ? "更新成功" : "更新失败");
        }

        private async Task<(bool, string)> HandleSettingMaxRetryAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var maxRetry) || maxRetry <= 0) {
                return (false, "请输入有效的正整数");
            }

            var retryConfig = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.MaxRetryCountKey);

            if (retryConfig == null) {
                await DataContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
                    Key = GeneralLLMService.MaxRetryCountKey,
                    Value = maxRetry.ToString()
                });
            } else {
                retryConfig.Value = maxRetry.ToString();
            }

            await DataContext.SaveChangesAsync();
            await redis.DeleteKeysAsync();
            return (true, $"最大重试次数已设置为: {maxRetry}");
        }

        private async Task<(bool, string)> HandleSettingMaxImageRetryAsync(EditLLMConfRedisHelper redis, string command) {
            if (!int.TryParse(command, out var maxImageRetry) || maxImageRetry <= 0) {
                return (true, "请输入有效的正整数");
            }

            var imageRetryConfig = await DataContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.MaxImageRetryCountKey);

            if (imageRetryConfig == null) {
                await DataContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
                    Key = GeneralLLMService.MaxImageRetryCountKey,
                    Value = maxImageRetry.ToString()
                });
            } else {
                imageRetryConfig.Value = maxImageRetry.ToString();
            }

            await DataContext.SaveChangesAsync();
            await redis.DeleteKeysAsync();
            return (true, $"图片处理最大重试次数已设置为: {maxImageRetry}");
        }


        public async Task<(bool, string)> ExecuteAsync(string Command, long ChatId) {
            var redis = new EditLLMConfRedisHelper(connectionMultiplexer, ChatId);
            var currentState = await redis.GetStateAsync();
            if (Command.Trim().Equals("刷新所有渠道", StringComparison.OrdinalIgnoreCase)) {
                var count = await Helper.RefreshAllChannel();
                return (true, $"已添加{count}个模型");
            } else if (Command.Trim().Equals("设置重试次数", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingMaxRetry.GetDescription());
                return (true, "请输入最大重试次数(默认100):");
            } else if (Command.Trim().Equals("设置图片重试次数", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingMaxImageRetry.GetDescription());
                return (true, "请输入图片处理最大重试次数(默认1000):");
            } else if (Command.Trim().Equals("设置图片模型", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingAltPhotoModel.GetDescription());
                return (true, "请输入图片分析使用的模型名称:");
            }

            if (Command.Trim().Equals("新建渠道", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.AwaitingName.GetDescription());
                return (true, "请输入渠道的名称");
            } else if (Command.Trim().Equals("编辑渠道", StringComparison.OrdinalIgnoreCase)) {
                var channels = await Helper.GetAllChannels();
                if (channels.Count == 0) {
                    return (true, "当前没有可编辑的渠道");
                }

                var sb = new StringBuilder();
                sb.AppendLine("请选择要编辑的渠道ID：");
                foreach (var channel in channels) {
                    sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                }

                await redis.SetStateAsync(LLMConfState.EditingSelectChannel.GetDescription());
                return (true, sb.ToString());
            } else if (Command.Trim().Equals("添加模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await Helper.GetAllChannels();
                if (channels.Count == 0) {
                    return (true, "当前没有可添加模型的渠道");
                }

                var sb = new StringBuilder();
                sb.AppendLine("请选择要添加模型的渠道ID：");
                foreach (var channel in channels) {
                    sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                }

                await redis.SetStateAsync(LLMConfState.AddingModelSelectChannel.GetDescription());
                return (true, sb.ToString());
            } else if (Command.Trim().Equals("移除模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await Helper.GetAllChannels();
                if (channels.Count == 0) {
                    return (true, "当前没有可移除模型的渠道");
                }

                var sb = new StringBuilder();
                sb.AppendLine("请选择要移除模型的渠道ID：");
                foreach (var channel in channels) {
                    sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                }

                await redis.SetStateAsync(LLMConfState.RemovingModelSelectChannel.GetDescription());
                return (true, sb.ToString());
            } else if (Command.Trim().Equals("查看模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await Helper.GetAllChannels();
                if (channels.Count == 0) {
                    return (true, "当前没有可查看模型的渠道");
                }

                var sb = new StringBuilder();
                sb.AppendLine("请选择要查看模型的渠道ID：");
                foreach (var channel in channels) {
                    sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                }

                await redis.SetStateAsync(LLMConfState.ViewingModelSelectChannel.GetDescription());
                return (true, sb.ToString());
            }

            if (string.IsNullOrEmpty(currentState)) {
                return (false, "请先选择一个操作");
            }

            switch (currentState) {
                case var _ when currentState == LLMConfState.AwaitingName.GetDescription():
                    return await HandleAwaitingNameAsync(redis, Command);

                case var _ when currentState == LLMConfState.AwaitingGateway.GetDescription():
                    return await HandleAwaitingGatewayAsync(redis, Command);

                case var _ when currentState == LLMConfState.AwaitingProvider.GetDescription():
                    return await HandleAwaitingProviderAsync(redis, Command);

                case var _ when currentState == LLMConfState.AwaitingParallel.GetDescription():
                    return await HandleAwaitingParallelAsync(redis, Command);

                case var _ when currentState == LLMConfState.AwaitingPriority.GetDescription():
                    return await HandleAwaitingPriorityAsync(redis, Command);

                case var _ when currentState == LLMConfState.AwaitingApiKey.GetDescription():
                    return await HandleAwaitingApiKeyAsync(redis, Command);

                case var _ when currentState == LLMConfState.SettingAltPhotoModel.GetDescription():
                    return await HandleSettingAltPhotoModelAsync(redis, Command);

                case var _ when currentState == LLMConfState.EditingSelectChannel.GetDescription():
                    return await HandleEditingSelectChannelAsync(redis, Command);

                case var _ when currentState == LLMConfState.EditingSelectField.GetDescription():
                    return await HandleEditingSelectFieldAsync(redis, Command);

                case var _ when currentState == LLMConfState.AddingModelSelectChannel.GetDescription():
                    return await HandleAddingModelSelectChannelAsync(redis, Command);

                case var _ when currentState == LLMConfState.AddingModelInput.GetDescription():
                    return await HandleAddingModelInputAsync(redis, Command);

                case var _ when currentState == LLMConfState.RemovingModelSelectChannel.GetDescription():
                    return await HandleRemovingModelSelectChannelAsync(redis, Command);

                case var _ when currentState == LLMConfState.RemovingModelSelect.GetDescription():
                    return await HandleRemovingModelSelectAsync(redis, Command);

                case var _ when currentState == LLMConfState.ViewingModelSelectChannel.GetDescription():
                    return await HandleViewingModelSelectChannelAsync(redis, Command);

                case var _ when currentState == LLMConfState.EditingInputValue.GetDescription():
                    return await HandleEditingInputValueAsync(redis, Command);

                case var _ when currentState == LLMConfState.SettingMaxRetry.GetDescription():
                    return await HandleSettingMaxRetryAsync(redis, Command);

                case var _ when currentState == LLMConfState.SettingMaxImageRetry.GetDescription():
                    return await HandleSettingMaxImageRetryAsync(redis, Command);

                default:
                    // 非预期状态或初始状态
                    return (false, "");
            }
        }
    }
}
