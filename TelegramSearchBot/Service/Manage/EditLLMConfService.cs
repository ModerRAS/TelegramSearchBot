using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.Manage {
    public class EditLLMConfService : IService {
        public string ServiceName => "EditLLMConfService";
        protected readonly DataDbContext DataContext;
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        protected OllamaService OllamaService { get; set; }
        protected GeminiService GeminiService { get; set; }
        protected OpenAIService OpenAIService { get; set; }
        public EditLLMConfService(
            DataDbContext context, 
            IConnectionMultiplexer connectionMultiplexer,
            OpenAIService openAIService,
            OllamaService ollamaService,
            GeminiService geminiService
            ) {
            DataContext = context;
            this.connectionMultiplexer = connectionMultiplexer;
            OpenAIService = openAIService;
            OllamaService = ollamaService;
            GeminiService = geminiService;
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
                IEnumerable<string> models;
                switch (Provider) {
                    case LLMProvider.OpenAI:
                        models = await OpenAIService.GetAllModels(channel);
                        break;
                    case LLMProvider.Ollama:
                        models = await OllamaService.GetAllModels(channel);
                        break;
                    case LLMProvider.Gemini:
                        models = await GeminiService.GetAllModels(channel);
                        break;
                    default:
                        return -1;
                }
                var list = new List<ChannelWithModel>();
                foreach (var e in models) {
                    list.Add(new ChannelWithModel() { LLMChannelId = channel.Id, ModelName = e });
                }
                await DataContext.ChannelsWithModel.AddRangeAsync(list);
                await DataContext.SaveChangesAsync();
                return channel.Id;
            }
            catch {
                return -1;
            }
        }

        public async Task<int> RefreshAllChannel() {
            var count = 0;
            var channels = from s in DataContext.LLMChannels
                           select s;
            IEnumerable<string> models;
            foreach (var channel in channels)
            {
                switch (channel.Provider)
                {
                    case LLMProvider.OpenAI:
                        models = await OpenAIService.GetAllModels(channel);
                        break;
                    case LLMProvider.Ollama:
                        models = await OllamaService.GetAllModels(channel);
                        break;
                    case LLMProvider.Gemini:
                        models = await GeminiService.GetAllModels(channel);
                        break;
                    default:
                        continue;
                }

                var list = new List<ChannelWithModel>();

                foreach (var model in models)
                {
                    bool exists = await DataContext.ChannelsWithModel
                        .AnyAsync(x => x.LLMChannelId == channel.Id && x.ModelName == model);

                    if (!exists)
                    {
                        list.Add(new ChannelWithModel
                        {
                            LLMChannelId = channel.Id,
                            ModelName = model
                        });
                    }
                }

                if (list.Any())
                {
                    await DataContext.ChannelsWithModel.AddRangeAsync(list);
                    count += list.Count;
                }
            }

            await DataContext.SaveChangesAsync();
            return count;
        }

        /// <summary>
        /// 获取所有LLM通道列表
        /// </summary>
        /// <returns>包含所有LLM通道的列表，如果查询失败返回空列表</returns>
        public async Task<List<LLMChannel>> GetAllChannels() {
            try {
                return await DataContext.LLMChannels.ToListAsync();
            }
            catch {
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
            }
            catch {
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
            }
            catch {
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
                }
                catch {
                    return false;
                }
            }
            else {
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
                }
                catch {
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
                }
                catch {
                    return false;
                }
            }
            else {
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
                }
                catch {
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
                }
                catch {
                    return false;
                }
            }
            else {
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
                }
                catch {
                    await transaction.RollbackAsync();
                    return false;
                }
            }
        }
        private class RedisHelper {
            private readonly IConnectionMultiplexer _connection;
            private readonly long _chatId;
            private IDatabase _database;

            public RedisHelper(IConnectionMultiplexer connection, long chatId) {
                _connection = connection;
                _chatId = chatId;
                _database = _connection.GetDatabase();
            }

            private IDatabase GetDatabase() => _database;

            private string StateKey => $"llmconf:{_chatId}:state";
            private string DataKey => $"llmconf:{_chatId}:data";

            public async Task<string?> GetStateAsync() {
                return await GetDatabase().StringGetAsync(StateKey);
            }

            public async Task SetStateAsync(string state) {
                await GetDatabase().StringSetAsync(StateKey, state);
            }

            public async Task<string?> GetDataAsync() {
                return await GetDatabase().StringGetAsync(DataKey);
            }

            public async Task SetDataAsync(string data) {
                await GetDatabase().StringSetAsync(DataKey, data);
            }

            public async Task DeleteKeysAsync() {
                var db = GetDatabase();
                await db.KeyDeleteAsync(StateKey);
                await db.KeyDeleteAsync(DataKey);
            }
        }

        public async Task<(bool, string)> ExecuteAsync(string Command, long ChatId) {
            var redis = new RedisHelper(connectionMultiplexer, ChatId);
            var currentState = await redis.GetStateAsync();
            if (Command.Trim().Equals("刷新所有渠道", StringComparison.OrdinalIgnoreCase)) {
                var count = await RefreshAllChannel();
                return (true, $"已添加{count}个模型");
            }
            else if (Command.Trim().Equals("设置重试次数", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingMaxRetry.GetDescription());
                return (true, "请输入最大重试次数(默认100):");
            }
            else if (Command.Trim().Equals("设置图片重试次数", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingMaxImageRetry.GetDescription());
                return (true, "请输入图片处理最大重试次数(默认1000):");
            }
            else if (Command.Trim().Equals("设置图片模型", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.SettingAltPhotoModel.GetDescription());
                return (true, "请输入图片分析使用的模型名称:");
            }
            
            if (Command.Trim().Equals("新建渠道", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(LLMConfState.AwaitingName.GetDescription());
                return (true, "请输入渠道的名称");
            }
            else if (Command.Trim().Equals("编辑渠道", StringComparison.OrdinalIgnoreCase)) {
                var channels = await GetAllChannels();
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
            }
            else if (Command.Trim().Equals("添加模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await GetAllChannels();
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
            }
            else if (Command.Trim().Equals("移除模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await GetAllChannels();
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
            }
            else if (Command.Trim().Equals("查看模型", StringComparison.OrdinalIgnoreCase)) {
                var channels = await GetAllChannels();
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

            switch (currentState.ToString()) {
                case var _ when currentState == LLMConfState.AwaitingName.GetDescription():
                    await redis.SetDataAsync(Command); // 存储名称
                    await redis.SetStateAsync(LLMConfState.AwaitingGateway.GetDescription());
                    return (true, "请输入渠道地址");
                
                case var _ when currentState == LLMConfState.AwaitingGateway.GetDescription():
                    var name = await redis.GetDataAsync();
                    await redis.SetDataAsync($"{name}|{Command}"); // 追加地址
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
                
                case var _ when currentState == LLMConfState.AwaitingProvider.GetDescription():
                    var nameAndGateway = (await redis.GetDataAsync()).Split('|');
                    LLMProvider provider;
                    var validProviders = Enum.GetValues(typeof(LLMProvider))
                        .Cast<LLMProvider>()
                        .Where(p => p != LLMProvider.None)
                        .ToList();
                    
                    if (int.TryParse(Command.Trim(), out int providerIndex) && 
                        providerIndex > 0 && providerIndex <= validProviders.Count) {
                        provider = validProviders[providerIndex - 1];
                    } else {
                        return (false, $"无效的类型选择，请输入1到{validProviders.Count}之间的数字");
                    }
                    await redis.SetDataAsync($"{nameAndGateway[0]}|{nameAndGateway[1]}|{provider}");
                    await redis.SetStateAsync(LLMConfState.AwaitingParallel.GetDescription());
                    return (true, "请输入渠道的最大并行数量(默认1):");
                
                case var _ when currentState == LLMConfState.AwaitingParallel.GetDescription():
                    int parallel = string.IsNullOrEmpty(Command) ? 1 : int.Parse(Command);
                    var data = await redis.GetDataAsync();
                    var parts = data.Split('|');
                    await redis.SetDataAsync($"{parts[0]}|{parts[1]}|{parts[2]}|{parallel}");
                    await redis.SetStateAsync(LLMConfState.AwaitingPriority.GetDescription());
                    return (true, "请输入渠道的优先级(默认0):");
                
                case var _ when currentState == LLMConfState.AwaitingPriority.GetDescription():
                    int priority = string.IsNullOrEmpty(Command) ? 0 : int.Parse(Command);
                    data = await redis.GetDataAsync();
                    parts = data.Split('|');
                    await redis.SetDataAsync($"{parts[0]}|{parts[1]}|{parts[2]}|{parts[3]}|{priority}");
                    await redis.SetStateAsync(LLMConfState.AwaitingApiKey.GetDescription());
                    return (true, "请输入渠道的API Key");
                
                case var _ when currentState == LLMConfState.AwaitingApiKey.GetDescription():
                    data = await redis.GetDataAsync();
                    parts = data.Split('|');
                    var apiKey = Command;
                    provider = (LLMProvider)Enum.Parse(typeof(LLMProvider), parts[2]);
                    
                    // 创建渠道
                    var result = await AddChannel(
                        parts[0], 
                        parts[1], 
                        apiKey, 
                        provider,
                        int.Parse(parts[3]),
                        int.Parse(parts[4]));
                    
                    // 清理状态
                    await redis.DeleteKeysAsync();
                    
                    return (true, result > 0 ? "渠道创建成功" : "渠道创建失败");
                
                case var _ when currentState == LLMConfState.SettingAltPhotoModel.GetDescription():
                    try {
                        var config = await DataContext.AppConfigurationItems
                            .FirstOrDefaultAsync(x => x.Key == GeneralLLMService.AltPhotoModelName);
                        
                        if (config == null) {
                            await DataContext.AppConfigurationItems.AddAsync(new Model.Data.AppConfigurationItem {
                                Key = GeneralLLMService.AltPhotoModelName,
                                Value = Command
                            });
                        } else {
                            config.Value = Command;
                        }
                        
                        await DataContext.SaveChangesAsync();
                        await redis.DeleteKeysAsync();
                        return (true, $"图片分析模型已设置为: {Command}");
                    } catch {
                        return (false, "设置图片分析模型失败");
                    }
                
                case var _ when currentState == LLMConfState.EditingSelectChannel.GetDescription():
                    if (!int.TryParse(Command, out var channelId)) {
                        return (false, "请输入有效的渠道ID");
                    }
                    
                    var channel = await GetChannelById(channelId);
                    if (channel == null) {
                        return (false, "找不到指定的渠道");
                    }
                    
                    await redis.SetDataAsync(channelId.ToString());
                    await redis.SetStateAsync(LLMConfState.EditingSelectField.GetDescription());
                    return (true, $"请选择要编辑的字段：\n1. 名称 ({channel.Name})\n2. 地址 ({channel.Gateway})\n3. 类型 ({channel.Provider})\n4. API Key\n5. 最大并行数量 ({channel.Parallel})\n6. 优先级 ({channel.Priority})");
                
                case var _ when currentState == LLMConfState.EditingSelectField.GetDescription():
                    var value = await redis.GetDataAsync();
                    var editChannelId = int.Parse(value);
                    await redis.SetDataAsync($"{editChannelId}|{Command}");
                    
                    if (Command == "3") {
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
                    }
                    else {
                        await redis.SetStateAsync(LLMConfState.EditingInputValue.GetDescription());
                        return (true, "请输入新的值：");
                    }
                
                case var _ when currentState == LLMConfState.AddingModelSelectChannel.GetDescription():
                    if (!int.TryParse(Command, out var addModelChannelId)) {
                        return (false, "请输入有效的渠道ID");
                    }
                    
                    var addModelChannel = await GetChannelById(addModelChannelId);
                    if (addModelChannel == null) {
                        return (false, "找不到指定的渠道");
                    }
                    
                    await redis.SetDataAsync(addModelChannelId.ToString());
                    await redis.SetStateAsync(LLMConfState.AddingModelInput.GetDescription());
                    return (true, "请输入要添加的模型名称，多个模型用逗号或分号分隔");
                
                case var _ when currentState == LLMConfState.AddingModelInput.GetDescription():
                    var addChannelId = int.Parse(await redis.GetDataAsync());
                    var ModelResult = await AddModelWithChannel(addChannelId, Command);
                    
                    // 清理状态
                    await redis.DeleteKeysAsync();
                    
                    return (true, ModelResult ? "模型添加成功" : "模型添加失败");
                
                case var _ when currentState == LLMConfState.RemovingModelSelectChannel.GetDescription():
                    if (!int.TryParse(Command, out var removeModelChannelId)) {
                        return (false, "请输入有效的渠道ID");
                    }
                    
                    var removeModelChannel = await GetChannelById(removeModelChannelId);
                    if (removeModelChannel == null) {
                        return (false, "找不到指定的渠道");
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
                
                case var _ when currentState == LLMConfState.RemovingModelSelect.GetDescription():
                    data = await redis.GetDataAsync();
                    parts = data.Split('|');
                    var removeChannelId = int.Parse(parts[0]);
                    var modelList = parts[1].Split(',');
                    
                    if (!int.TryParse(Command, out var modelIndex) || modelIndex < 1 || modelIndex > modelList.Length) {
                        return (false, "请输入有效的模型序号");
                    }
                    
                    var modelName = modelList[modelIndex - 1];
                    var removeResult = await RemoveModelFromChannel(removeChannelId, modelName);
                    
                    // 清理状态
                    await redis.DeleteKeysAsync();
                    
                    return (true, removeResult ? "模型移除成功" : "模型移除失败");
                
                case var _ when currentState == LLMConfState.ViewingModelSelectChannel.GetDescription():
                    if (!int.TryParse(Command, out var viewModelChannelId)) {
                        return (false, "请输入有效的渠道ID");
                    }
                    
                    var viewModelChannel = await GetChannelById(viewModelChannelId);
                    if (viewModelChannel == null) {
                        return (false, "找不到指定的渠道");
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
                
                case var _ when currentState == LLMConfState.EditingInputValue.GetDescription():
                    data = await redis.GetDataAsync();
                    parts = data.Split('|');
                    var editId = int.Parse(parts[0]);
                    var field = parts[1];
                    
                    bool updateResult = false;
                    switch (field) {
                        case "1":
                            updateResult = await UpdateChannel(editId, name: Command);
                            break;
                        case "2":
                            updateResult = await UpdateChannel(editId, gateway: Command);
                            break;
                        case "3":
                            if (!Enum.TryParse<LLMProvider>(Command, out var newProvider)) {
                                return (true, "无效的类型");
                            }
                            updateResult = await UpdateChannel(editId, provider: newProvider);
                            break;
                        case "4":
                            updateResult = await UpdateChannel(editId, apiKey: Command);
                            break;
                        case "5":
                            if (!int.TryParse(Command, out var tmp_parallel)) {
                                return (false, "请输入有效的数字");
                            }
                            updateResult = await UpdateChannel(editId, parallel: tmp_parallel);
                            break;
                        case "6":
                            if (!int.TryParse(Command, out var tmp_priority)) {
                                return (false, "请输入有效的数字");
                            }
                            updateResult = await UpdateChannel(editId, priority: tmp_priority);
                            break;
                        default:
                            return (false, "无效的字段选择");
                    }
                    
                    // 清理状态
                    await redis.DeleteKeysAsync();
                    
                    return (true, updateResult ? "更新成功" : "更新失败");
                
                case var _ when currentState == LLMConfState.SettingMaxRetry.GetDescription():
                    if (!int.TryParse(Command, out var maxRetry) || maxRetry <= 0) {
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

                case var _ when currentState == LLMConfState.SettingMaxImageRetry.GetDescription():
                    if (!int.TryParse(Command, out var maxImageRetry) || maxImageRetry <= 0) {
                        return (false, "请输入有效的正整数");
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

                default:
                    // 非预期状态或初始状态
                    return (false, "");
            }
        }
    }
}
