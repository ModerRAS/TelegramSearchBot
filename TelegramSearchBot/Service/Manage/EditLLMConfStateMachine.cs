using Stateless;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace TelegramSearchBot.Service.Manage {
    public class EditLLMConfStateMachine {
        private readonly DataDbContext _dataContext;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly OpenAIService _openAIService;
        
        private enum State {
            Initial,
            AwaitingName,
            AwaitingGateway,
            AwaitingProvider,
            AwaitingParallel,
            AwaitingPriority,
            AwaitingApiKey,
            EditingSelectChannel,
            EditingSelectField,
            EditingInputValue,
            AddingModelSelectChannel,
            AddingModelInput,
            RemovingModelSelectChannel,
            RemovingModelSelect,
            ViewingModelSelectChannel
        }

        private enum Trigger {
            CreateChannel,
            EditChannel,
            AddModel,
            RemoveModel,
            ViewModels,
            InputReceived,
            Cancel
        }

        private StateMachine<State, Trigger> _machine;
        private long _chatId;
        private string _stateKey;
        private string _dataKey;
        private IDatabase _db;

        public EditLLMConfStateMachine(
            DataDbContext dataContext,
            IConnectionMultiplexer connectionMultiplexer,
            OpenAIService openAIService,
            long chatId) {
            
            _dataContext = dataContext;
            _connectionMultiplexer = connectionMultiplexer;
            _openAIService = openAIService;
            _chatId = chatId;
            _db = connectionMultiplexer.GetDatabase();
            _stateKey = $"llmconf:{chatId}:state";
            _dataKey = $"llmconf:{chatId}:data";

            _machine = new StateMachine<State, Trigger>(State.Initial);

            ConfigureStateMachine();
        }

        private void ConfigureStateMachine() {
            // 初始状态配置
            _machine.Configure(State.Initial)
                .Permit(Trigger.CreateChannel, State.AwaitingName)
                .Permit(Trigger.EditChannel, State.EditingSelectChannel)
                .Permit(Trigger.AddModel, State.AddingModelSelectChannel)
                .Permit(Trigger.RemoveModel, State.RemovingModelSelectChannel)
                .Permit(Trigger.ViewModels, State.ViewingModelSelectChannel);

            // 创建渠道流程
            _machine.Configure(State.AwaitingName)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_name"))
                .Permit(Trigger.InputReceived, State.AwaitingGateway);

            _machine.Configure(State.AwaitingGateway)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_gateway"))
                .Permit(Trigger.InputReceived, State.AwaitingProvider);

            _machine.Configure(State.AwaitingProvider)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_provider"))
                .Permit(Trigger.InputReceived, State.AwaitingParallel);

            _machine.Configure(State.AwaitingParallel)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_parallel"))
                .Permit(Trigger.InputReceived, State.AwaitingPriority);

            _machine.Configure(State.AwaitingPriority)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_priority"))
                .Permit(Trigger.InputReceived, State.AwaitingApiKey);

            _machine.Configure(State.AwaitingApiKey)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "awaiting_apikey"))
                .Permit(Trigger.InputReceived, State.Initial);

            // 编辑渠道流程
            _machine.Configure(State.EditingSelectChannel)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "editing_select_channel"))
                .Permit(Trigger.InputReceived, State.EditingSelectField);

            _machine.Configure(State.EditingSelectField)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "editing_select_field"))
                .Permit(Trigger.InputReceived, State.EditingInputValue);

            _machine.Configure(State.EditingInputValue)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "editing_input_value"))
                .Permit(Trigger.InputReceived, State.Initial);

            // 添加模型流程
            _machine.Configure(State.AddingModelSelectChannel)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "adding_model_select_channel"))
                .Permit(Trigger.InputReceived, State.AddingModelInput);

            _machine.Configure(State.AddingModelInput)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "adding_model_input"))
                .Permit(Trigger.InputReceived, State.Initial);

            // 移除模型流程
            _machine.Configure(State.RemovingModelSelectChannel)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "removing_model_select_channel"))
                .Permit(Trigger.InputReceived, State.RemovingModelSelect);

            _machine.Configure(State.RemovingModelSelect)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "removing_model_select"))
                .Permit(Trigger.InputReceived, State.Initial);

            // 查看模型流程
            _machine.Configure(State.ViewingModelSelectChannel)
                .OnEntryAsync(async () => await _db.StringSetAsync(_stateKey, "viewing_model_select_channel"))
                .Permit(Trigger.InputReceived, State.Initial);

            // 取消操作
            _machine.Configure(State.AwaitingName)
                .Permit(Trigger.Cancel, State.Initial);
            
            _machine.Configure(State.AwaitingGateway)
                .Permit(Trigger.Cancel, State.Initial);
            
            // 其他状态的取消操作...
        }

        public async Task<(bool, string)> ProcessCommand(string command) {
            try {
                if (command.Trim().Equals("新建渠道", StringComparison.OrdinalIgnoreCase)) {
                    await _machine.FireAsync(Trigger.CreateChannel);
                    return (true, "请输入渠道的名称");
                }
                else if (command.Trim().Equals("编辑渠道", StringComparison.OrdinalIgnoreCase)) {
                    await _machine.FireAsync(Trigger.EditChannel);
                    var channels = await GetAllChannels();
                    if (channels.Count == 0) {
                        return (true, "当前没有可编辑的渠道");
                    }
                    
                    var sb = new StringBuilder();
                    sb.AppendLine("请选择要编辑的渠道ID：");
                    foreach (var channel in channels) {
                        sb.AppendLine($"{channel.Id}. {channel.Name} ({channel.Provider})");
                    }
                    return (true, sb.ToString());
                }
                // 其他命令处理...

                // 处理输入
                if (_machine.State != State.Initial) {
                    await _machine.FireAsync(Trigger.InputReceived);
                    return await HandleStateInput(command);
                }

                return (false, "");
            }
            catch (Exception ex) {
                return (false, $"处理命令时出错: {ex.Message}");
            }
        }

        private async Task<(bool, string)> HandleStateInput(string input) {
            switch (_machine.State) {
                case State.AwaitingName:
                    await _db.StringSetAsync(_dataKey, input);
                    return (true, "请输入渠道地址");
                
                case State.AwaitingGateway:
                    var name = await _db.StringGetAsync(_dataKey);
                    await _db.StringSetAsync(_dataKey, $"{name}|{input}");
                    return (true, await GetDynamicProviderOptions());
                
                case State.AwaitingProvider:
                    var nameAndGateway = (await _db.StringGetAsync(_dataKey)).ToString().Split('|');
                    LLMProvider provider;
                    var validProviders = Enum.GetValues(typeof(LLMProvider))
                        .Cast<LLMProvider>()
                        .Where(p => p != LLMProvider.None)
                        .ToList();
                    
                    if (int.TryParse(input.Trim(), out int providerIndex) && 
                        providerIndex > 0 && providerIndex <= validProviders.Count) {
                        provider = validProviders[providerIndex - 1];
                    } else {
                        return (false, $"无效的类型选择，请输入1到{validProviders.Count}之间的数字");
                    }
                    await _db.StringSetAsync(_dataKey, $"{nameAndGateway[0]}|{nameAndGateway[1]}|{provider}");
                    return (true, "请输入渠道的最大并行数量(默认1):");
                
                case State.AwaitingParallel:
                    int parallel = string.IsNullOrEmpty(input) ? 1 : int.Parse(input);
                    var parts = (await _db.StringGetAsync(_dataKey)).ToString().Split('|');
                    await _db.StringSetAsync(_dataKey, $"{parts[0]}|{parts[1]}|{parts[2]}|{parallel}");
                    return (true, "请输入渠道的优先级(默认0):");
                
                case State.AwaitingPriority:
                    int priority = string.IsNullOrEmpty(input) ? 0 : int.Parse(input);
                    parts = (await _db.StringGetAsync(_dataKey)).ToString().Split('|');
                    await _db.StringSetAsync(_dataKey, $"{parts[0]}|{parts[1]}|{parts[2]}|{parts[3]}|{priority}");
                    return (true, "请输入渠道的API Key");
                
                case State.AwaitingApiKey:
                    parts = (await _db.StringGetAsync(_dataKey)).ToString().Split('|');
                    var apiKey = input;
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
                    await _db.KeyDeleteAsync(_stateKey);
                    await _db.KeyDeleteAsync(_dataKey);
                    
                    return (true, result > 0 ? "渠道创建成功" : "渠道创建失败");
                default:
                    return (false, "");
            }
        }

        // 辅助方法
        private async Task<string> GetDynamicProviderOptions() {
            var providerOptions = new StringBuilder();
            var providerType = typeof(LLMProvider);
            var providers = Enum.GetValues(providerType)
                .Cast<LLMProvider>()
                .Where(p => p != LLMProvider.None)
                .Select((p, i) => $"{i + 1}. {p}");
            
            providerOptions.AppendLine("请选择渠道类型：");
            providerOptions.AppendJoin("\n", providers);
            return providerOptions.ToString();
        }

        private async Task<List<LLMChannel>> GetAllChannels() {
            try {
                return await _dataContext.LLMChannels.ToListAsync();
            }
            catch {
                return new List<LLMChannel>();
            }
        }

        private async Task<LLMChannel?> GetChannelById(int id) {
            try {
                return await _dataContext.LLMChannels.FindAsync(id);
            }
            catch {
                return null;
            }
        }

        private async Task<int> AddChannel(string name, string gateway, string apiKey, LLMProvider provider, int parallel = 1, int priority = 0) {
            try {
                var channel = new LLMChannel {
                    Name = name,
                    Gateway = gateway,
                    ApiKey = apiKey,
                    Provider = provider,
                    Parallel = parallel,
                    Priority = priority
                };
                
                await _dataContext.LLMChannels.AddAsync(channel);
                await _dataContext.SaveChangesAsync();
                var models = await _openAIService.GetAllModels(channel);
                var list = new List<ChannelWithModel>();
                foreach (var e in models) {
                    list.Add(new ChannelWithModel() { LLMChannelId = channel.Id, ModelName = e });
                }
                await _dataContext.ChannelsWithModel.AddRangeAsync(list);
                await _dataContext.SaveChangesAsync();
                return channel.Id;
            }
            catch {
                return -1;
            }
        }

        private async Task<bool> UpdateChannel(int channelId, string? name = null, string? gateway = null, 
            string? apiKey = null, LLMProvider? provider = null, int? parallel = null, int? priority = null) {
            using var transaction = await _dataContext.Database.BeginTransactionAsync();
            try {
                var channel = await _dataContext.LLMChannels.FindAsync(channelId);
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

                await _dataContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch {
                await transaction.RollbackAsync();
                return false;
            }
        }
    }
}
