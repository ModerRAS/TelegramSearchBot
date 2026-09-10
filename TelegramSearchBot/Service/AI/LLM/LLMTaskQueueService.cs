using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class LLMTaskQueueService : IService {
        private readonly DataDbContext _dbContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly ChunkPollingService _chunkPollingService;
        private readonly AgentRegistryService _agentRegistryService;
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly ILogger<LLMTaskQueueService> _logger;

        public LLMTaskQueueService(
            DataDbContext dbContext,
            IConnectionMultiplexer redis,
            ChunkPollingService chunkPollingService,
            AgentRegistryService agentRegistryService,
            LlmVisibilityService llmVisibilityService = null,
            ILogger<LLMTaskQueueService> logger = null) {
            _dbContext = dbContext;
            _redis = redis;
            _chunkPollingService = chunkPollingService;
            _agentRegistryService = agentRegistryService;
            _llmVisibilityService = llmVisibilityService;
            _logger = logger;
        }

        public string ServiceName => nameof(LLMTaskQueueService);

        public async Task<AgentTaskStreamHandle> EnqueueMessageTaskAsync(
            TelegramMessage telegramMessage,
            string botName,
            long botUserId,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(telegramMessage);

            var inputMessage = string.IsNullOrWhiteSpace(telegramMessage.Text)
                ? telegramMessage.Caption ?? string.Empty
                : telegramMessage.Text;
            var task = await BuildMessageTaskAsync(
                telegramMessage.Chat.Id,
                telegramMessage.From?.Id ?? 0,
                telegramMessage.MessageId,
                telegramMessage.Date,
                inputMessage,
                botName,
                botUserId,
                cancellationToken);
            await _agentRegistryService.EnsureAgentAsync(task.ChatId, cancellationToken);
            return await EnqueueTaskAsync(task);
        }

        public async Task<AgentTaskStreamHandle> EnqueueMessageTaskAsync(
            long chatId,
            long userId,
            long messageId,
            DateTime messageDate,
            string inputMessage,
            string botName,
            long botUserId,
            CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(inputMessage)) {
                throw new ArgumentException("Input message cannot be empty.", nameof(inputMessage));
            }

            var task = await BuildMessageTaskAsync(
                chatId,
                userId,
                messageId,
                messageDate,
                inputMessage,
                botName,
                botUserId,
                cancellationToken);
            await _agentRegistryService.EnsureAgentAsync(task.ChatId, cancellationToken);
            return await EnqueueTaskAsync(task);
        }

        public async Task<AgentTaskStreamHandle> EnqueueContinuationTaskAsync(
            LlmContinuationSnapshot snapshot,
            string botName,
            long botUserId,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(snapshot);

            var channelInfo = await LoadChannelAsync(snapshot.ModelName, snapshot.ChannelId, cancellationToken);
            var task = new AgentExecutionTask {
                Kind = AgentTaskKind.Continuation,
                TaskId = Guid.NewGuid().ToString("N"),
                ChatId = snapshot.ChatId,
                UserId = snapshot.UserId,
                MessageId = snapshot.OriginalMessageId,
                BotName = botName,
                BotUserId = botUserId,
                ModelName = snapshot.ModelName,
                MaxToolCycles = Env.MaxToolCycles,
                Channel = channelInfo,
                ContinuationSnapshot = snapshot
            };

            await _agentRegistryService.EnsureAgentAsync(task.ChatId, cancellationToken);
            return await EnqueueTaskAsync(task);
        }

        public async Task<AgentTaskStreamHandle> EnqueueSyntheticMessageTaskAsync(
            long chatId,
            long userId,
            long messageId,
            string inputMessage,
            string botName,
            long botUserId,
            DateTime createdAtUtc,
            CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(inputMessage)) {
                throw new ArgumentException("Synthetic LLM input cannot be empty.", nameof(inputMessage));
            }

            var modelName = await _dbContext.GroupSettings.AsNoTracking()
                .Where(x => x.GroupId == chatId)
                .Select(x => x.LLMModelName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new InvalidOperationException("请先为当前群组设置模型。");
            }

            var channelInfo = await LoadChannelAsync(modelName, null, cancellationToken);
            var history = await LoadHistoryAsync(chatId, cancellationToken);
            var task = new AgentExecutionTask {
                TaskId = Guid.NewGuid().ToString("N"),
                Kind = AgentTaskKind.Message,
                ChatId = chatId,
                UserId = userId,
                MessageId = messageId,
                BotName = botName,
                BotUserId = botUserId,
                ModelName = modelName,
                InputMessage = inputMessage,
                MaxToolCycles = Env.MaxToolCycles,
                Channel = channelInfo,
                History = history,
                CreatedAtUtc = createdAtUtc
            };

            await _agentRegistryService.EnsureAgentAsync(task.ChatId, cancellationToken);
            return await EnqueueTaskAsync(task);
        }

        private async Task<AgentTaskStreamHandle> EnqueueTaskAsync(AgentExecutionTask task) {
            var db = _redis.GetDatabase();
            var payload = JsonConvert.SerializeObject(task);
            await db.ListLeftPushAsync(LlmAgentRedisKeys.AgentTaskQueue, payload);
            await db.HashSetAsync(LlmAgentRedisKeys.AgentTaskState(task.TaskId), [
                new HashEntry("status", AgentTaskStatus.Pending.ToString()),
                new HashEntry("chatId", task.ChatId),
                new HashEntry("messageId", task.MessageId),
                new HashEntry("modelName", task.ModelName),
                new HashEntry("createdAtUtc", task.CreatedAtUtc.ToString("O")),
                new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O")),
                new HashEntry("payload", payload),
                new HashEntry("recoveryCount", 0),
                new HashEntry("maxRecoveryAttempts", Env.AgentMaxRecoveryAttempts),
                new HashEntry("lastContent", string.Empty)
            ]);

            return _chunkPollingService.TrackTask(task.TaskId);
        }

        private async Task<AgentExecutionTask> BuildMessageTaskAsync(
            long chatId,
            long userId,
            long messageId,
            DateTime messageDate,
            string inputMessage,
            string botName,
            long botUserId,
            CancellationToken cancellationToken) {
            var modelName = await _dbContext.GroupSettings.AsNoTracking()
                .Where(x => x.GroupId == chatId)
                .Select(x => x.LLMModelName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new InvalidOperationException("请先为当前群组设置模型。");
            }

            var channelInfo = await LoadChannelAsync(modelName, null, cancellationToken);
            var history = await LoadHistoryAsync(chatId, cancellationToken);
            return new AgentExecutionTask {
                TaskId = Guid.NewGuid().ToString("N"),
                Kind = AgentTaskKind.Message,
                ChatId = chatId,
                UserId = userId,
                MessageId = messageId,
                BotName = botName,
                BotUserId = botUserId,
                ModelName = modelName,
                InputMessage = inputMessage,
                MaxToolCycles = Env.MaxToolCycles,
                Channel = channelInfo,
                History = history,
                CreatedAtUtc = messageDate.ToUniversalTime()
            };
        }

        private async Task<AgentChannelConfig> LoadChannelAsync(string modelName, int? channelId, CancellationToken cancellationToken) {
            var rows = await _dbContext.ChannelsWithModel.AsNoTracking()
                .Include(x => x.ApiBinding)
                .Include(x => x.LLMChannel).ThenInclude(c => c.Bindings)
                .Include(x => x.Capabilities)
                .Where(x => !x.IsDeleted && x.ModelName == modelName)
                .ToListAsync(cancellationToken);

            if (channelId.HasValue) {
                rows = rows.Where(x => x.LLMChannelId == channelId.Value).ToList();
            }

            if (rows.Count == 0) {
                throw new InvalidOperationException($"找不到模型 {modelName} 可用的渠道配置。");
            }

            // 确定性路由：渠道 Priority DESC，渠道内按 IsPreferred/IsDefault/binding.Id 解析（与 General 路径同一 resolver）
            var route = LlmRouteResolver.ResolveFirst(
                rows
                    .OrderByDescending(x => x.LLMChannel.Priority)
                    .GroupBy(x => x.LLMChannelId)
                    .Select(g => (g.First().LLMChannel, g.ToList())),
                modelName,
                _logger);

            if (route == null) {
                throw new InvalidOperationException($"找不到模型 {modelName} 可用的渠道配置。");
            }

            return new AgentChannelConfig {
                ChannelId = route.Channel.Id,
                Name = route.Channel.Name,
                Gateway = route.Channel.Gateway,
                ApiKey = route.Channel.ApiKey,
                Provider = route.Channel.Provider,
                Parallel = route.Channel.Parallel,
                Priority = route.Channel.Priority,
                ModelName = route.Model.ModelName,
                BindingId = route.Binding?.Id,
                BindingEndpoint = route.Binding?.Endpoint ?? string.Empty,
                BindingProtocol = route.Binding?.Protocol,
                BindingAuthProfile = route.Binding?.AuthProfile,
                Capabilities = route.Model.Capabilities
                    .Select(x => new AgentModelCapability {
                        Name = x.CapabilityName,
                        Value = x.CapabilityValue,
                        Description = x.Description ?? string.Empty
                    })
                    .ToList()
            };
        }

        private async Task<List<AgentHistoryMessage>> LoadHistoryAsync(long chatId, CancellationToken cancellationToken) {
            var history = await _dbContext.Messages.AsNoTracking()
                .Where(x => x.GroupId == chatId && x.DateTime > DateTime.UtcNow.AddHours(-1))
                .OrderBy(x => x.DateTime)
                .ToListAsync(cancellationToken);

            if (history.Count < 10) {
                history = await _dbContext.Messages.AsNoTracking()
                    .Where(x => x.GroupId == chatId)
                    .OrderByDescending(x => x.DateTime)
                    .Take(10)
                    .OrderBy(x => x.DateTime)
                    .ToListAsync(cancellationToken);
            }

            if (_llmVisibilityService != null) {
                history = await _llmVisibilityService.FilterVisibleMessagesAsync(chatId, history, cancellationToken);
            }

            var userIds = history.Select(x => x.FromUserId).Distinct().ToList();
            var users = await _dbContext.UserData.AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var messageIds = history.Select(x => x.Id).ToList();
            var extensionRecords = await _dbContext.MessageExtensions.AsNoTracking()
                .Where(x => messageIds.Contains(x.MessageDataId))
                .ToListAsync(cancellationToken);
            var extensions = extensionRecords
                .GroupBy(x => x.MessageDataId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(e => new AgentMessageExtensionSnapshot {
                        Name = e.Name,
                        Value = e.Value
                    }).ToList());

            return history.Select(message => {
                users.TryGetValue(message.FromUserId, out var user);
                extensions.TryGetValue(message.Id, out var messageExtensions);
                return new AgentHistoryMessage {
                    DataId = message.Id,
                    DateTime = message.DateTime,
                    GroupId = message.GroupId,
                    MessageId = message.MessageId,
                    FromUserId = message.FromUserId,
                    ReplyToUserId = message.ReplyToUserId,
                    ReplyToMessageId = message.ReplyToMessageId,
                    Content = message.Content ?? string.Empty,
                    User = new AgentUserSnapshot {
                        UserId = user?.Id ?? message.FromUserId,
                        FirstName = user?.FirstName ?? string.Empty,
                        LastName = user?.LastName ?? string.Empty,
                        UserName = user?.UserName ?? string.Empty,
                        IsBot = user?.IsBot,
                        IsPremium = user?.IsPremium
                    },
                    Extensions = messageExtensions ?? []
                };
            }).ToList();
        }
    }
}
