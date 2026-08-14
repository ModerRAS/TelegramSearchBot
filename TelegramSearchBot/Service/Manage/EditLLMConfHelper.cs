using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Manage;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
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

                // 管理修复：每渠道恰好一个默认 binding（blueprint §六.8/§八-阶段3）
                var defaultBinding = await EnsureDefaultBinding(channel);

                _logger.LogInformation("成功添加新通道: {ChannelName} ({Provider})", Name, Provider);

                IEnumerable<string> models;
                var service = _LLMFactory.GetLLMService(Provider);
                if (service == null) {
                    _logger.LogWarning("未找到提供商 {Provider} 的LLM服务", Provider);
                    return -1;
                }

                // Catalog ≠ Entitlement（blueprint §四.5）：OpenCode 目录不自动创建模型行，
                // 管理员通过“添加模型”手工维护授权集合。
                if (IsOpenCodeBinding(defaultBinding)) {
                    _logger.LogInformation("通道 {ChannelName} 默认 binding 为 OpenCode 目录（opencode.ai/zen/*），不自动创建模型行，请手工添加模型", Name);
                } else {
                    models = await service.GetAllModels(channel);
                    var list = new List<ChannelWithModel>();
                    foreach (var e in models) {
                        list.Add(new ChannelWithModel() { LLMChannelId = channel.Id, ModelName = e, IsDeleted = false, AuthorizationSource = AuthorizationSource.Discovered, ApiBindingId = defaultBinding?.Id });
                    }
                    await DataContext.ChannelsWithModel.AddRangeAsync(list);
                    await DataContext.SaveChangesAsync();

                    _logger.LogInformation("为新通道 {ChannelName} 添加了 {Count} 个模型", Name, list.Count);
                }

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
            var channels = await DataContext.LLMChannels
                .Include(c => c.Bindings)
                .ToListAsync();

            _logger.LogInformation("开始刷新所有通道的模型和能力信息...");

            foreach (var channel in channels) {
                // 管理修复：每渠道恰好一个默认 binding（blueprint §六.8）
                var defaultBinding = await EnsureDefaultBinding(channel);

                // Catalog ≠ Entitlement（blueprint §四.1/.5）：OpenCode /models 不是授权快照，
                // 刷新不得创建、不得软删任何模型行；能力 metadata 仍可安全 merge（不会创建/复活行）。
                if (IsOpenCodeBinding(defaultBinding)) {
                    _logger.LogInformation("通道 {ChannelName} 默认 binding 为 OpenCode 目录（opencode.ai/zen/*），跳过目录创建/软删", channel.Name);
                    await TryUpdateCapabilitiesAsync(channel.Id);
                    continue;
                }

                var service = _LLMFactory.GetLLMService(channel.Provider);
                if (service == null) {
                    _logger.LogWarning("未找到通道 {ChannelName} ({Provider}) 的LLM服务", channel.Name, channel.Provider);
                    continue;
                }

                _logger.LogInformation("正在刷新通道: {ChannelName} ({Provider})", channel.Name, channel.Provider);

                try {
                    IEnumerable<string> models = await service.GetAllModels(channel);
                    var modelSet = models.ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // 获取该通道下所有已有记录（包含已软删除的）
                    var existingRecords = await DataContext.ChannelsWithModel
                        .Where(x => x.LLMChannelId == channel.Id)
                        .ToListAsync();

                    // 本刷新只作用于同 binding（默认路由）的 Discovered 行；
                    // Manual 行永不被刷新软删/复活（blueprint §四.5）。
                    var scopedRecords = existingRecords
                        .Where(x => x.AuthorizationSource == AuthorizationSource.Discovered
                                 && x.ApiBindingId == defaultBinding?.Id)
                        .ToList();

                    // 恢复之前被标记删除但现在重新出现的模型（仅 Discovered 行）
                    foreach (var record in scopedRecords.Where(x => x.IsDeleted && modelSet.Contains(x.ModelName))) {
                        record.IsDeleted = false;
                        count++;
                        _logger.LogInformation("通道 {ChannelName} 恢复模型 {ModelName}", channel.Name, record.ModelName);
                    }

                    // 标记已有 Discovered 记录中不再存在于 API 的模型为已删除；
                    // MiniMax 动态发现可能暂时不可用，不软删 MiniMax 模型（#387）；
                    // Manual 行永不被刷新软删（blueprint §四.5）。
                    foreach (var record in scopedRecords.Where(x => !x.IsDeleted && !modelSet.Contains(x.ModelName) && channel.Provider != LLMProvider.MiniMax)) {
                        record.IsDeleted = true;
                        _logger.LogInformation("通道 {ChannelName} 标记删除消失的模型 {ModelName}", channel.Name, record.ModelName);
                    }

                    // 添加全新的模型（同 binding 内忽略大小写去重，blueprint §七.6）
                    var toAdd = modelSet
                        .Where(m => !scopedRecords.Any(r => r.ModelName.Equals(m, StringComparison.OrdinalIgnoreCase)))
                        .Select(m => new ChannelWithModel {
                            LLMChannelId = channel.Id,
                            ModelName = m,
                            IsDeleted = false,
                            AuthorizationSource = AuthorizationSource.Discovered,
                            ApiBindingId = defaultBinding?.Id
                        })
                        .ToList();

                    if (toAdd.Any()) {
                        await DataContext.ChannelsWithModel.AddRangeAsync(toAdd);
                        count += toAdd.Count;
                        _logger.LogInformation("为通道 {ChannelName} 添加了 {Count} 个新模型", channel.Name, toAdd.Count);
                    }

                    // 保存变更
                    await DataContext.SaveChangesAsync();
                } catch (Exception ex) {
                    // 抓取失败：整 channel 跳过，不产生任何创建/软删（Manual 与 Discovered 均不受影响）
                    _logger.LogError(ex, "刷新通道 {ChannelName} ({Provider}) 时出错", channel.Name, channel.Provider);
                }

                await TryUpdateCapabilitiesAsync(channel.Id);
            }

            _logger.LogInformation("完成刷新所有通道，共添加/恢复了 {Count} 个模型", count);
            return count;
        }

        private async Task TryUpdateCapabilitiesAsync(int channelId) {
            // 刷新此通道的模型能力信息
            _logger.LogInformation("正在更新通道 {ChannelId} 的模型能力信息...", channelId);
            bool capabilityUpdateSuccess = await _modelCapabilityService.UpdateChannelModelCapabilities(channelId);

            if (capabilityUpdateSuccess) {
                _logger.LogInformation("成功更新通道 {ChannelId} 的模型能力信息", channelId);
            } else {
                _logger.LogWarning("更新通道 {ChannelId} 的模型能力信息失败", channelId);
            }
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
                    var rows = await DataContext.ChannelsWithModel
                        .Where(m => m.LLMChannelId == channelId)
                        .ToListAsync();
                    var model = rows.FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));

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
                    var rows = await DataContext.ChannelsWithModel
                        .Where(m => m.LLMChannelId == channelId)
                        .ToListAsync();
                    var model = rows.FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));

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
        public async Task<bool> AddModelWithChannel(int channelId, List<string> modelNames) {
            if (modelNames == null || modelNames.Count == 0) {
                return false;
            }

            // 管理员手工添加 = Manual 授权（blueprint §四.5）；新行关联默认 binding
            var defaultBinding = await DataContext.LLMApiBindings
                .Where(b => b.LLMChannelId == channelId && b.IsDefault)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync();

            // Skip transaction for InMemory database
            if (DataContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") {
                try {
                    var existingRows = await DataContext.ChannelsWithModel
                        .Where(m => m.LLMChannelId == channelId)
                        .ToListAsync();
                    foreach (var modelName in modelNames) {
                        // 忽略大小写去重（blueprint §七.6），不新建重复行
                        var existing = existingRows.FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) {
                            existing.IsDeleted = false;
                        } else {
                            await DataContext.ChannelsWithModel.AddAsync(new ChannelWithModel {
                                LLMChannelId = channelId,
                                ModelName = modelName,
                                IsDeleted = false,
                                ApiBindingId = defaultBinding?.Id
                            });
                        }
                    }
                    await DataContext.SaveChangesAsync();
                    return true;
                } catch {
                    return false;
                }
            } else {
                using var transaction = await DataContext.Database.BeginTransactionAsync();
                try {
                    var existingRows = await DataContext.ChannelsWithModel
                        .Where(m => m.LLMChannelId == channelId)
                        .ToListAsync();
                    foreach (var modelName in modelNames) {
                        // 忽略大小写去重（blueprint §七.6），不新建重复行
                        var existing = existingRows.FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) {
                            existing.IsDeleted = false;
                        } else {
                            await DataContext.ChannelsWithModel.AddAsync(new ChannelWithModel {
                                LLMChannelId = channelId,
                                ModelName = modelName,
                                IsDeleted = false,
                                ApiBindingId = defaultBinding?.Id
                            });
                        }
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

                    var gatewayChanged = !string.IsNullOrWhiteSpace(gateway) && gateway != channel.Gateway;
                    var providerChanged = provider.HasValue && provider.Value != channel.Provider;

                    if (!string.IsNullOrWhiteSpace(name)) {
                        channel.Name = name;
                    }
                    if (gatewayChanged) {
                        channel.Gateway = gateway;
                    }
                    if (!string.IsNullOrWhiteSpace(apiKey)) {
                        channel.ApiKey = apiKey;
                    }
                    if (providerChanged) {
                        channel.Provider = provider.Value;
                    }
                    if (parallel.HasValue) {
                        channel.Parallel = parallel.Value;
                    }
                    if (priority.HasValue) {
                        channel.Priority = priority.Value;
                    }

                    // 镜像规则（blueprint §七）：channel Gateway/Provider 变更时同步默认 binding
                    if (gatewayChanged || providerChanged) {
                        await SyncDefaultBindingAsync(channel);
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

                    var gatewayChanged = !string.IsNullOrWhiteSpace(gateway) && gateway != channel.Gateway;
                    var providerChanged = provider.HasValue && provider.Value != channel.Provider;

                    if (!string.IsNullOrWhiteSpace(name)) {
                        channel.Name = name;
                    }
                    if (gatewayChanged) {
                        channel.Gateway = gateway;
                    }
                    if (!string.IsNullOrWhiteSpace(apiKey)) {
                        channel.ApiKey = apiKey;
                    }
                    if (providerChanged) {
                        channel.Provider = provider.Value;
                    }
                    if (parallel.HasValue) {
                        channel.Parallel = parallel.Value;
                    }
                    if (priority.HasValue) {
                        channel.Priority = priority.Value;
                    }

                    // 镜像规则（blueprint §七）：channel Gateway/Provider 变更时同步默认 binding
                    if (gatewayChanged || providerChanged) {
                        await SyncDefaultBindingAsync(channel);
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

        /// <summary>
        /// 获取渠道下的模型展示名列表。同一模型（忽略大小写）跨多个 binding 时显示为
        /// `model [channel/binding/protocol]`，单 binding 模型保持原名（blueprint §八-阶段3）。
        /// </summary>
        public async Task<List<string>> GetModelsByChannelId(long channelId) {
            var rows = await DataContext.ChannelsWithModel
                .Include(c => c.ApiBinding)
                .Include(c => c.LLMChannel)
                .Where(c => c.LLMChannelId == channelId && !c.IsDeleted)
                .ToListAsync();

            var multiBindingNames = rows
                .GroupBy(r => r.ModelName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return rows
                .Select(r => multiBindingNames.Contains(r.ModelName) ? FormatModelDisplay(r) : r.ModelName)
                .ToList();
        }

        /// <summary>
        /// 多 binding 展示格式：`model [channel/binding/protocol]`；legacy 行（无 binding）以 default/Provider 标注。
        /// </summary>
        private static string FormatModelDisplay(ChannelWithModel row) {
            var channelName = row.LLMChannel?.Name ?? row.LLMChannelId.ToString();
            var bindingLabel = row.ApiBindingId?.ToString() ?? "default";
            var protocolLabel = row.ApiBinding?.Protocol.ToString()
                                ?? row.LLMChannel?.Provider.ToString()
                                ?? "unknown";
            return $"{row.ModelName} [{channelName}/{bindingLabel}/{protocolLabel}]";
        }

        /// <summary>
        /// 管理修复：确保每渠道恰好一个默认 binding（blueprint §六.8）。
        /// 无默认 binding 时按 legacy Provider/Gateway 映射补建（与迁移的 Protocol/AuthProfile 映射一致）；
        /// 多个 IsDefault 时保留 Id 最小者并降级其余（告警，稳定解析）。
        /// </summary>
        private async Task<LLMApiBinding?> EnsureDefaultBinding(LLMChannel channel) {
            await DataContext.Entry(channel).Collection(c => c.Bindings).LoadAsync();
            var defaults = channel.Bindings.Where(b => b.IsDefault).OrderBy(b => b.Id).ToList();
            if (defaults.Count > 1) {
                _logger.LogWarning("渠道 {ChannelId} 存在多个 IsDefault binding（{Count} 个），保留 Id 最小者，其余降级（请管理员修复）", channel.Id, defaults.Count);
                foreach (var other in defaults.Skip(1)) {
                    other.IsDefault = false;
                }
            }
            if (defaults.Count >= 1) {
                return defaults[0];
            }

            var (protocol, authProfile) = MapProviderToBinding(channel.Provider);
            var binding = new LLMApiBinding {
                LLMChannelId = channel.Id,
                Endpoint = channel.Gateway,
                Protocol = protocol,
                AuthProfile = authProfile,
                IsDefault = true
            };
            DataContext.LLMApiBindings.Add(binding);
            channel.Bindings.Add(binding);
            await DataContext.SaveChangesAsync();
            _logger.LogInformation("为渠道 {ChannelName} 补建默认 binding（Endpoint={Endpoint}, Protocol={Protocol}, AuthProfile={AuthProfile}）", channel.Name, channel.Gateway, protocol, authProfile);
            return binding;
        }

        /// <summary>
        /// 设置（或创建）默认 binding，保证每渠道至多一个 IsDefault；
        /// 并镜像 LLMChannel.Gateway/Provider，使旧二进制继续走默认协议（blueprint §七）。
        /// </summary>
        public async Task<bool> SetDefaultBinding(int channelId, string endpoint, LlmProtocol protocol, LlmAuthProfile authProfile) {
            try {
                var channel = await DataContext.LLMChannels.Include(c => c.Bindings).FirstOrDefaultAsync(c => c.Id == channelId);
                if (channel == null) {
                    return false;
                }

                var defaults = channel.Bindings.Where(b => b.IsDefault).OrderBy(b => b.Id).ToList();
                var binding = defaults.FirstOrDefault();
                if (defaults.Count > 1) {
                    _logger.LogWarning("渠道 {ChannelId} 存在多个 IsDefault binding（{Count} 个），保留 Id 最小者，其余降级（请管理员修复）", channelId, defaults.Count);
                    foreach (var other in defaults.Skip(1)) {
                        other.IsDefault = false;
                    }
                }
                if (binding == null) {
                    binding = new LLMApiBinding { LLMChannelId = channelId, IsDefault = true };
                    DataContext.LLMApiBindings.Add(binding);
                    channel.Bindings.Add(binding);
                }

                binding.Endpoint = endpoint;
                binding.Protocol = protocol;
                binding.AuthProfile = authProfile;

                // 镜像规则（blueprint §七）：默认 binding 变更同步 Gateway/Provider，旧二进制继续可用
                channel.Gateway = endpoint;
                channel.Provider = MapProtocolToProvider(protocol);

                await DataContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "设置渠道 {ChannelId} 默认 binding 失败", channelId);
                return false;
            }
        }

        /// <summary>
        /// 模型级协议覆盖：同一渠道同一模型（忽略大小写）至多一个 IsPreferred 行。
        /// 目标行必须已存在且未删除；已有其他 preferred 行时降级并告警（遵循 phase-2 resolver 的告警+稳定解析约定）。
        /// </summary>
        public async Task<bool> SetModelPreferred(int channelId, string modelName, int bindingId) {
            try {
                var rows = await DataContext.ChannelsWithModel
                    .Where(m => m.LLMChannelId == channelId && !m.IsDeleted)
                    .ToListAsync();

                var target = rows.FirstOrDefault(r => r.ApiBindingId == bindingId
                                                    && r.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));
                if (target == null) {
                    _logger.LogWarning("设置 preferred 失败：渠道 {ChannelId} 模型 {ModelName} binding {BindingId} 无可用行", channelId, modelName, bindingId);
                    return false;
                }

                var others = rows.Where(r => r.IsPreferred && r.Id != target.Id
                                          && r.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (others.Count > 0) {
                    _logger.LogWarning("模型 {ModelName} 渠道 {ChannelId} 存在多个 IsPreferred 行（{Count} 个），已降级其余行，保持稳定解析（请管理员修复）", modelName, channelId, others.Count + 1);
                    foreach (var other in others) {
                        other.IsPreferred = false;
                    }
                }

                target.IsPreferred = true;
                await DataContext.SaveChangesAsync();
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "设置模型 {ModelName} preferred 失败", modelName);
                return false;
            }
        }

        /// <summary>
        /// channel Gateway/Provider 变更时同步默认 binding（镜像规则，blueprint §七）。
        /// 无默认 binding 时不创建（补建由 EnsureDefaultBinding 在 AddChannel/RefreshAllChannel 负责）。
        /// </summary>
        private async Task SyncDefaultBindingAsync(LLMChannel channel) {
            var defaultBinding = await DataContext.LLMApiBindings
                .Where(b => b.LLMChannelId == channel.Id && b.IsDefault)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync();
            if (defaultBinding == null) {
                return;
            }
            defaultBinding.Endpoint = channel.Gateway;
            (defaultBinding.Protocol, defaultBinding.AuthProfile) = MapProviderToBinding(channel.Provider);
        }

        /// <summary>
        /// OpenCode 目录识别：持久化 binding Endpoint 位于 opencode.ai/zen/* 空间（数据属性，非协议猜测，blueprint §四.1）。
        /// 更干净的标记需新增 binding 列（如 IsCatalogOnly），需要迁移，超出阶段3范围。
        /// </summary>
        internal static bool IsOpenCodeBinding(LLMApiBinding? binding) {
            return binding != null
                && Uri.TryCreate(binding.Endpoint, UriKind.Absolute, out var uri)
                && string.Equals(uri.Host, "opencode.ai", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/zen/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// legacy Provider → (Protocol, AuthProfile)，与迁移 SQL 的映射一致（blueprint §七.4/.5）。
        /// </summary>
        internal static (LlmProtocol Protocol, LlmAuthProfile AuthProfile) MapProviderToBinding(LLMProvider provider) {
            return provider switch {
                LLMProvider.OpenAI => (LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer),
                LLMProvider.Ollama => (LlmProtocol.Ollama, LlmAuthProfile.None),
                LLMProvider.Gemini => (LlmProtocol.Gemini, LlmAuthProfile.Bearer),
                LLMProvider.MiniMax => (LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer),
                LLMProvider.LMStudio => (LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer),
                LLMProvider.Anthropic => (LlmProtocol.AnthropicMessages, LlmAuthProfile.AnthropicApiKey),
                LLMProvider.ResponsesAPI => (LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer),
                _ => (LlmProtocol.OpenAIChat, LlmAuthProfile.Bearer)
            };
        }

        /// <summary>
        /// Protocol → legacy Provider（SetDefaultBinding 镜像用；OpenAIChat 的 OpenAIChat→OpenAI 为通用默认）。
        /// </summary>
        internal static LLMProvider MapProtocolToProvider(LlmProtocol protocol) {
            return protocol switch {
                LlmProtocol.OpenAIChat => LLMProvider.OpenAI,
                LlmProtocol.OpenAIResponses => LLMProvider.ResponsesAPI,
                LlmProtocol.AnthropicMessages => LLMProvider.Anthropic,
                LlmProtocol.Ollama => LLMProvider.Ollama,
                LlmProtocol.Gemini => LLMProvider.Gemini,
                _ => LLMProvider.OpenAI
            };
        }
    }
}
