using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramMessage = Telegram.Bot.Types.Message;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class LLMTaskQueueService : IService {
        private readonly DataDbContext _dbContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly ChunkPollingService _chunkPollingService;
        private readonly AgentRegistryService _agentRegistryService;

        public LLMTaskQueueService(
            DataDbContext dbContext,
            IConnectionMultiplexer redis,
            ChunkPollingService chunkPollingService,
            AgentRegistryService agentRegistryService) {
            _dbContext = dbContext;
            _redis = redis;
            _chunkPollingService = chunkPollingService;
            _agentRegistryService = agentRegistryService;
        }

        public string ServiceName => nameof(LLMTaskQueueService);

        public async Task<AgentTaskStreamHandle> EnqueueMessageTaskAsync(
            TelegramMessage telegramMessage,
            string botName,
            long botUserId,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(telegramMessage);

            var task = await BuildMessageTaskAsync(telegramMessage, botName, botUserId, cancellationToken);
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
            TelegramMessage telegramMessage,
            string botName,
            long botUserId,
            CancellationToken cancellationToken) {
            var modelName = await _dbContext.GroupSettings.AsNoTracking()
                .Where(x => x.GroupId == telegramMessage.Chat.Id)
                .Select(x => x.LLMModelName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new InvalidOperationException("请先为当前群组设置模型。");
            }

            var channelInfo = await LoadChannelAsync(modelName, null, cancellationToken);
            var history = await LoadHistoryAsync(telegramMessage.Chat.Id, cancellationToken);
            return new AgentExecutionTask {
                TaskId = Guid.NewGuid().ToString("N"),
                Kind = AgentTaskKind.Message,
                ChatId = telegramMessage.Chat.Id,
                UserId = telegramMessage.From?.Id ?? 0,
                MessageId = telegramMessage.MessageId,
                BotName = botName,
                BotUserId = botUserId,
                ModelName = modelName,
                InputMessage = string.IsNullOrWhiteSpace(telegramMessage.Text) ? telegramMessage.Caption ?? string.Empty : telegramMessage.Text,
                MaxToolCycles = Env.MaxToolCycles,
                Channel = channelInfo,
                History = history,
                CreatedAtUtc = telegramMessage.Date.ToUniversalTime()
            };
        }

        private async Task<AgentChannelConfig> LoadChannelAsync(string modelName, int? channelId, CancellationToken cancellationToken) {
            var query = _dbContext.ChannelsWithModel.AsNoTracking()
                .Include(x => x.LLMChannel)
                .Include(x => x.Capabilities)
                .Where(x => !x.IsDeleted && x.ModelName == modelName);

            if (channelId.HasValue) {
                query = query.Where(x => x.LLMChannelId == channelId.Value);
            }

            var channelWithModel = await query
                .OrderByDescending(x => x.LLMChannel.Priority)
                .FirstOrDefaultAsync(cancellationToken);

            if (channelWithModel?.LLMChannel == null) {
                throw new InvalidOperationException($"找不到模型 {modelName} 可用的渠道配置。");
            }

            return new AgentChannelConfig {
                ChannelId = channelWithModel.LLMChannel.Id,
                Name = channelWithModel.LLMChannel.Name,
                Gateway = channelWithModel.LLMChannel.Gateway,
                ApiKey = channelWithModel.LLMChannel.ApiKey,
                Provider = channelWithModel.LLMChannel.Provider,
                Parallel = channelWithModel.LLMChannel.Parallel,
                Priority = channelWithModel.LLMChannel.Priority,
                ModelName = channelWithModel.ModelName,
                Capabilities = channelWithModel.Capabilities
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
