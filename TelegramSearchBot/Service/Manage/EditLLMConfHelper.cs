using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Interface.AI.LLM;
using TelegramSearchBot.Core.Interface.Manage;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.AI;
using TelegramSearchBot.Core.Model.Data;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class EditLLMConfHelper : IService, IEditLLMConfHelper {
        public string ServiceName => "EditLLMConfHelper";
        protected readonly DataDbContext DataContext;
        private readonly ILLMFactory _LLMFactory;
        private readonly IModelCapabilityService _modelCapabilityService;
        private readonly ILogger<EditLLMConfHelper> _logger;

        public EditLLMConfHelper(
            DataDbContext context,
            ILLMFactory llmFactory,
            IModelCapabilityService modelCapabilityService,
            ILogger<EditLLMConfHelper> logger
            ) {
            DataContext = context;
            _LLMFactory = llmFactory;
            _modelCapabilityService = modelCapabilityService;
            _logger = logger;
        }
        /// <summary>
        /// 添加一个新的LLM通道到数据库
        /// </summary>
        /// <param name="Name">通道名称</param>
        /// <param name="Gateway">网关地址</param>
        /// <param name="ApiKey">API密钥</param>
        /// <param name="Provider">LLM提供商</param>
        /// <returns>成功返回添加记录的Id，失败返回-1</returns>
        public async Task<int> AddChannel(string Name, string Gateway, string ApiKey, LLMProvider Provider, int Parallel = 1, int Priority = 0) {
            try {
                var channel = new LLMChannel {
                    Name = Name,
                    Gateway = Gateway,
                    ApiKey = ApiKey,
                    Provider = Provider,
                    Parallel = Parallel,
                    Priority = Priority
                };

                await DataContext.LLMChannels.AddAsync(channel);
                await DataContext.SaveChangesAsync();

                _logger.LogInformation("成功添加新通道: {ChannelName} ({Provider})", Name, Provider);

                IEnumerable<string> models;
                var service = _LLMFactory.GetLLMService(Provider);
                if (service == null) {
                    _logger.LogWarning("未找到提供商 {Provider} 的LLM服务", Provider);
                    return -1;
                }

                models = await service.GetAllModels(channel);
                var list = new List<ChannelWithModel>();
                foreach (var e in models) {
                    list.Add(new ChannelWithModel() { LLMChannelId = channel.Id, ModelName = e });
                }
                await DataContext.ChannelsWithModel.AddRangeAsync(list);
                await DataContext.SaveChangesAsync();

                _logger.LogInformation("为新通道 {ChannelName} 添加了 {Count} 个模型", Name, list.Count);

                // 获取并存储模型能力信息
                _logger.LogInformation("正在获取通道 {ChannelName} 的模型能力信息...", Name);
                bool capabilityUpdateSuccess = await _modelCapabilityService.UpdateChannelModelCapabilities(channel.Id);

                if (capabilityUpdateSuccess) {
                    _logger.LogInformation("成功获取通道 {ChannelName} 的模型能力信息", Name);
                } else {
                    _logger.LogWarning("获取通道 {ChannelName} 的模型能力信息失败", Name);
                }

                return channel.Id;
            } catch (Exception ex) {
                _logger.LogError(ex, "添加通道 {Name} ({Provider}) 时出错", Name, Provider);
                return -1;
            }
        }

        public async Task<int> RefreshAllChannel() {
            var count = 0;
            var channels = from s in DataContext.LLMChannels
                           select s;
            IEnumerable<string> models;

            _logger.LogInformation("开始刷新所有通道的模型和能力信息...");

            foreach (var channel in channels) {
                var service = _LLMFactory.GetLLMService(channel.Provider);
                if (service == null) {
                    _logger.LogWarning("未找到通道 {ChannelName} ({Provider}) 的LLM服务", channel.Name, channel.Provider);
                    continue;
                }

                _logger.LogInformation("正在刷新通道: {ChannelName} ({Provider})", channel.Name, channel.Provider);

                try {
                    models = await service.GetAllModels(channel);

                    var list = new List<ChannelWithModel>();

                    foreach (var model in models) {
                        bool exists = await DataContext.ChannelsWithModel
                            .AnyAsync(x => x.LLMChannelId == channel.Id && x.ModelName == model);

                        if (!exists) {
                            list.Add(new ChannelWithModel {
                                LLMChannelId = channel.Id,
                                ModelName = model
                            });
                        }
                    }

                    if (list.Any()) {
                        await DataContext.ChannelsWithModel.AddRangeAsync(list);
                        count += list.Count;
                        _logger.LogInformation("为通道 {ChannelName} 添加了 {Count} 个新模型", channel.Name, list.Count);
                    }

                    // 保存新模型到数据库
                    await DataContext.SaveChangesAsync();

                    // 刷新此通道的模型能力信息
                    _logger.LogInformation("正在更新通道 {ChannelName} 的模型能力信息...", channel.Name);
                    bool capabilityUpdateSuccess = await _modelCapabilityService.UpdateChannelModelCapabilities(channel.Id);

                    if (capabilityUpdateSuccess) {
                        _logger.LogInformation("成功更新通道 {ChannelName} 的模型能力信息", channel.Name);
                    } else {
                        _logger.LogWarning("更新通道 {ChannelName} 的模型能力信息失败", channel.Name);
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "刷新通道 {ChannelName} ({Provider}) 时出错", channel.Name, channel.Provider);
                }
            }

            _logger.LogInformation("完成刷新所有通道，共添加了 {Count} 个新模型", count);
            return count;
        }

        /// <summary>
        /// 获取所有LLM通道列表
        /// </summary>
        /// <returns>包含所有LLM通道的列表，如果查询失败返回空列表</returns>
        public async Task<List<LLMChannel>> GetAllChannels() {
            try {
                return await DataContext.LLMChannels.ToListAsync();
            } catch {
                return new List<LLMChannel>();
            }
        }

        /// <summary>
        /// 根据ID获取单个LLM通道
        /// </summary>
        /// <param name="Id">通道ID</param>
        /// <returns>匹配的通道，如果未找到或查询失败返回null</returns>
        public async Task<LLMChannel?> GetChannelById(int Id) {
            try {
                return await DataContext.LLMChannels.FindAsync(Id);
            } catch {
                return null;
            }
        }

        /// <summary>
        /// 根据名称模糊查询LLM通道
        /// </summary>
        /// <param name="Name">通道名称</param>
        /// <returns>匹配的通道列表，如果查询失败返回空列表</returns>
        public async Task<List<LLMChannel>> GetChannelsByName(string Name) {
            try {
                return await DataContext.LLMChannels
                    .Where(c => c.Name.Contains(Name))
                    .ToListAsync();
            } catch {
                return new List<LLMChannel>();
            }
        }

        /// <summary>
        /// 批量添加模型与通道的关联关系(字符串形式)
        /// </summary>
        /// <param name="channelId">LLM通道ID</param>
        /// <param name="modelNames">要关联的模型名称字符串，用逗号或分号分隔</param>
        /// <returns>成功返回true，失败返回false</returns>
        public async Task<bool> AddModelWithChannel(int channelId, string modelNames) {
            if (string.IsNullOrWhiteSpace(modelNames)) {
                return false;
            }

            var models = modelNames.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            return await AddModelWithChannel(channelId, models);
        }

        /// <summary>
        /// 删除特定渠道中的特定模型关联
        /// </summary>
        /// <param name="channelId">渠道ID</param>
        /// <param name="modelName">要删除的模型名称</param>
        /// <returns>成功返回true，失败返回false</returns>
        public async Task<bool> RemoveModelFromChannel(int channelId, string modelName) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                return false;
            }

            // Skip transaction for InMemory database
            if (DataContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") {
                try {
                    var model = await DataContext.ChannelsWithModel
                        .FirstOrDefaultAsync(m => m.LLMChannelId == channelId && m.ModelName == modelName);

                    if (model != null) {
                        DataContext.ChannelsWithModel.Remove(model);
                        await DataContext.SaveChangesAsync();
                    }
                    return true;
                } catch {
                    return false;
                }
            } else {
                using var transaction = await DataContext.Database.BeginTransactionAsync();
                try {
                    var model = await DataContext.ChannelsWithModel
                        .FirstOrDefaultAsync(m => m.LLMChannelId == channelId && m.ModelName == modelName);

                    if (model != null) {
                        DataContext.ChannelsWithModel.Remove(model);
                        await DataContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    return true;
                } catch {
                    await transaction.RollbackAsync();
                    return false;
                }
            }
        }

        /// <summary>
        /// 批量添加模型与通道的关联关系(列表形式)
        /// </summary>
        /// <param name="channelId">LLM通道ID</param>
        /// <param name="modelNames">要关联的模型名称列表</param>
        /// <returns>成功返回true，失败返回false</returns>
        /// <summary>
        /// 更新LLM通道信息
        /// </summary>
        public async Task<bool> AddModelWithChannel(int channelId, List<string> modelNames) {
            if (modelNames == null || modelNames.Count == 0) {
                return false;
            }

            // Skip transaction for InMemory database
            if (DataContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") {
                try {
                    foreach (var modelName in modelNames) {
                        await DataContext.ChannelsWithModel.AddAsync(new ChannelWithModel {
                            LLMChannelId = channelId,
                            ModelName = modelName
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    return true;
                } catch {
                    return false;
                }
            } else {
                using var transaction = await DataContext.Database.BeginTransactionAsync();
                try {
                    foreach (var modelName in modelNames) {
                        await DataContext.ChannelsWithModel.AddAsync(new ChannelWithModel {
                            LLMChannelId = channelId,
                            ModelName = modelName
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                } catch {
                    await transaction.RollbackAsync();
                    return false;
                }
            }
        }

        /// <param name="channelId">要修改的通道ID</param>
        /// <param name="name">新名称(可选)</param>
        /// <param name="gateway">新网关地址(可选)</param>
        /// <param name="apiKey">新API密钥(可选)</param>
        /// <param name="provider">新提供商类型(可选)</param>
        /// <returns>成功返回true，失败返回false</returns>
        public async Task<bool> UpdateChannel(int channelId, string? name = null, string? gateway = null, string? apiKey = null, LLMProvider? provider = null, int? parallel = null, int? priority = null) {
            // Skip transaction for InMemory database
            if (DataContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") {
                try {
                    var channel = await DataContext.LLMChannels.FindAsync(channelId);
                    if (channel == null) {
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(name)) {
                        channel.Name = name;
                    }
                    if (!string.IsNullOrWhiteSpace(gateway)) {
                        channel.Gateway = gateway;
                    }
                    if (!string.IsNullOrWhiteSpace(apiKey)) {
                        channel.ApiKey = apiKey;
                    }
                    if (provider.HasValue) {
                        channel.Provider = provider.Value;
                    }
                    if (parallel.HasValue) {
                        channel.Parallel = parallel.Value;
                    }
                    if (priority.HasValue) {
                        channel.Priority = priority.Value;
                    }

                    await DataContext.SaveChangesAsync();
                    return true;
                } catch {
                    return false;
                }
            } else {
                using var transaction = await DataContext.Database.BeginTransactionAsync();
                try {
                    var channel = await DataContext.LLMChannels.FindAsync(channelId);
                    if (channel == null) {
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(name)) {
                        channel.Name = name;
                    }
                    if (!string.IsNullOrWhiteSpace(gateway)) {
                        channel.Gateway = gateway;
                    }
                    if (!string.IsNullOrWhiteSpace(apiKey)) {
                        channel.ApiKey = apiKey;
                    }
                    if (provider.HasValue) {
                        channel.Provider = provider.Value;
                    }
                    if (parallel.HasValue) {
                        channel.Parallel = parallel.Value;
                    }
                    if (priority.HasValue) {
                        channel.Priority = priority.Value;
                    }

                    await DataContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                } catch {
                    await transaction.RollbackAsync();
                    return false;
                }
            }
        }

        public async Task<List<string>> GetModelsByChannelId(long channelId) {
            var models = await DataContext.ChannelsWithModel
                .Where(c => c.LLMChannelId == channelId)
                .Select(c => c.ModelName)
                .ToListAsync();
            return models;
        }
    }
}
