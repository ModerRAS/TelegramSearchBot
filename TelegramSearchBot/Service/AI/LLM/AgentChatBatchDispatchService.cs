using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class AgentChatBatchDispatchService : BackgroundService {
        private const int MaxDueChatFetchCount = 20;
        private const int MaxDispatchConcurrency = 4;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LockRenewInterval = TimeSpan.FromSeconds(10);
        internal const string ReleaseLockScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('DEL', KEYS[1])
end
return 0";
        internal const string RenewLockScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
  redis.call('PEXPIRE', KEYS[1], ARGV[2])
  return 1
end
return 0";
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AgentChatBatchDispatchService> _logger;

        public AgentChatBatchDispatchService(
            IConnectionMultiplexer redis,
            IServiceProvider serviceProvider,
            ILogger<AgentChatBatchDispatchService> logger) {
            _redis = redis;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            using var timer = new PeriodicTimer(PollInterval);
            try {
                while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken)) {
                    await DispatchDueBatchesAsync(stoppingToken);
                }
            } catch (OperationCanceledException) {
                // Normal shutdown.
            }
        }

        internal async Task DispatchDueBatchesAsync(CancellationToken cancellationToken = default) {
            var db = _redis.GetDatabase();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var dueChatIds = await db.SortedSetRangeByScoreAsync(
                LlmAgentRedisKeys.AgentChatBatchDueSet,
                stop: now,
                order: Order.Ascending,
                take: MaxDueChatFetchCount);

            await Parallel.ForEachAsync(
                dueChatIds,
                new ParallelOptions {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = MaxDispatchConcurrency
                },
                async (chatIdValue, token) => {
                    token.ThrowIfCancellationRequested();
                    if (!long.TryParse(chatIdValue.ToString(), out var chatId)) {
                        await db.SortedSetRemoveAsync(LlmAgentRedisKeys.AgentChatBatchDueSet, chatIdValue);
                        return;
                    }

                    await TryDispatchChatBatchAsync(db, chatId, token);
                });
        }

        private async Task TryDispatchChatBatchAsync(IDatabase db, long chatId, CancellationToken cancellationToken) {
            var lockKey = LlmAgentRedisKeys.AgentChatBatchLock(chatId);
            var lockValue = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
            if (!await db.StringSetAsync(lockKey, lockValue, LockTtl, When.NotExists)) {
                return;
            }

            using var lockRenewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var lockRenewalTask = RenewLockUntilCanceledAsync(db, lockKey, lockValue, lockRenewalCts.Token);

            try {
                await db.SortedSetRemoveAsync(LlmAgentRedisKeys.AgentChatBatchDueSet, chatId.ToString());
                var dueAtValue = await db.HashGetAsync(LlmAgentRedisKeys.AgentChatBatchMeta(chatId), "dueAt");
                if (dueAtValue.HasValue
                    && long.TryParse(dueAtValue.ToString(), out var dueAt)
                    && dueAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) {
                    await db.SortedSetAddAsync(LlmAgentRedisKeys.AgentChatBatchDueSet, chatId.ToString(), dueAt);
                    return;
                }

                var bufferedMessages = await DrainBufferedMessagesAsync(db, chatId);
                if (bufferedMessages.Count == 0) {
                    await db.KeyDeleteAsync(LlmAgentRedisKeys.AgentChatBatchMeta(chatId));
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var groupSettings = scope.ServiceProvider.GetRequiredService<IGroupLlmSettingsService>();
                var settings = await groupSettings.GetAgentChatSettingsAsync(chatId, cancellationToken);
                if (!settings.IsEnabled || settings.Mode != GroupAgentChatMode.GuidedBatch) {
                    _logger.LogInformation("Dropping stale agent chat batch. ChatId={ChatId}, Enabled={Enabled}, Mode={Mode}",
                        chatId,
                        settings.IsEnabled,
                        settings.Mode);
                    return;
                }

                var llmVisibilityService = scope.ServiceProvider.GetRequiredService<LlmVisibilityService>();
                var visibleBufferedMessages = await FilterVisibleBufferedMessagesAsync(
                    llmVisibilityService,
                    chatId,
                    bufferedMessages,
                    cancellationToken);
                if (visibleBufferedMessages.Count == 0) {
                    _logger.LogInformation("Dropping agent chat batch after LLM invisibility filtering. ChatId={ChatId}", chatId);
                    await db.KeyDeleteAsync(LlmAgentRedisKeys.AgentChatBatchMeta(chatId));
                    return;
                }

                var last = visibleBufferedMessages[^1];
                var executionService = scope.ServiceProvider.GetRequiredService<AgentChatExecutionService>();
                await executionService.ExecuteAsync(new AgentChatExecutionRequest {
                    ReplyTarget = last.Message,
                    InputMessage = BuildBatchInput(visibleBufferedMessages),
                    BotName = last.BotName,
                    BotUserId = last.BotUserId,
                    ModelName = settings.ModelName ?? string.Empty,
                    Mode = GroupAgentChatMode.GuidedBatch
                }, cancellationToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogError(ex, "Failed to dispatch agent chat batch. ChatId={ChatId}", chatId);
            } finally {
                lockRenewalCts.Cancel();
                try {
                    await lockRenewalTask;
                } catch (OperationCanceledException) {
                    // Normal cancellation after dispatch exits.
                }

                await ReleaseLockIfOwnedAsync(db, lockKey, lockValue);
            }
        }

        private async Task RenewLockUntilCanceledAsync(IDatabase db, string lockKey, string lockValue, CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(LockRenewInterval);
            try {
                while (await timer.WaitForNextTickAsync(cancellationToken)) {
                    var renewed = await db.ScriptEvaluateAsync(
                        RenewLockScript,
                        new RedisKey[] { lockKey },
                        new RedisValue[] { lockValue, (long)LockTtl.TotalMilliseconds });
                    if (( int ) renewed != 1) {
                        _logger.LogWarning("Lost agent chat batch lock before dispatch completed. LockKey={LockKey}", lockKey);
                        return;
                    }
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Normal cancellation after dispatch exits.
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to renew agent chat batch lock. LockKey={LockKey}", lockKey);
            }
        }

        private static async Task ReleaseLockIfOwnedAsync(IDatabase db, string lockKey, string lockValue) {
            await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue });
        }

        private static async Task<List<AgentChatBufferedMessage>> DrainBufferedMessagesAsync(IDatabase db, long chatId) {
            var result = new List<AgentChatBufferedMessage>();
            var listKey = LlmAgentRedisKeys.AgentChatBatchList(chatId);

            while (true) {
                var value = await db.ListLeftPopAsync(listKey);
                if (!value.HasValue) {
                    break;
                }

                var buffered = JsonConvert.DeserializeObject<AgentChatBufferedMessage>(value.ToString());
                if (buffered?.Message != null && !string.IsNullOrWhiteSpace(buffered.Message.Content)) {
                    result.Add(buffered);
                }
            }

            return result
                .OrderBy(x => x.Message.DateTime)
                .ThenBy(x => x.Message.MessageId)
                .ToList();
        }

        private static async Task<List<AgentChatBufferedMessage>> FilterVisibleBufferedMessagesAsync(
            LlmVisibilityService llmVisibilityService,
            long chatId,
            List<AgentChatBufferedMessage> bufferedMessages,
            CancellationToken cancellationToken) {
            var invisibleUserIds = await llmVisibilityService.GetInvisibleUserIdsAsync(chatId, cancellationToken);
            if (invisibleUserIds.Count == 0) {
                return bufferedMessages;
            }

            return bufferedMessages
                .Where(x => !invisibleUserIds.Contains(x.Message.UserId))
                .ToList();
        }

        private static string BuildBatchInput(IReadOnlyList<AgentChatBufferedMessage> bufferedMessages) {
            var sb = new StringBuilder();
            sb.AppendLine("以下是群内短时间连续发送的多条消息，请作为同一次 Agent 输入处理。");
            sb.AppendLine("请结合所有消息理解用户意图，不要逐条机械回复。");

            for (var i = 0; i < bufferedMessages.Count; i++) {
                var message = bufferedMessages[i].Message;
                sb.AppendLine();
                sb.AppendLine($"--- 消息 {i + 1}/{bufferedMessages.Count} | MessageId={message.MessageId} | User={FormatUser(message)} | Time={message.DateTime:O} ---");
                sb.AppendLine(message.Content.Trim());
            }

            return sb.ToString().Trim();
        }

        private static string FormatUser(AgentChatMessageInput message) {
            var displayName = $"{message.FirstName} {message.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(displayName)) {
                displayName = string.IsNullOrWhiteSpace(message.Username) ? message.UserId.ToString() : $"@{message.Username}";
            }

            return $"{displayName} ({message.UserId})";
        }
    }
}
