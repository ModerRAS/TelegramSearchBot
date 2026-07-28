using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Singleton)]
    public sealed class AgentChatBatchQueueService : IService {
        private static readonly TimeSpan BatchTtl = TimeSpan.FromMinutes(30);
        private readonly IConnectionMultiplexer _redis;

        public AgentChatBatchQueueService(IConnectionMultiplexer redis) {
            _redis = redis;
        }

        public string ServiceName => nameof(AgentChatBatchQueueService);

        public async Task BufferAsync(
            AgentChatMessageInput message,
            string botName,
            long botUserId,
            int batchWindowSeconds,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var db = _redis.GetDatabase();
            var buffered = new AgentChatBufferedMessage {
                Message = message,
                BotName = botName,
                BotUserId = botUserId
            };
            var dueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, batchWindowSeconds)).ToUnixTimeMilliseconds();
            var chatIdMember = message.ChatId.ToString();

            await db.ListRightPushAsync(LlmAgentRedisKeys.AgentChatBatchList(message.ChatId), JsonConvert.SerializeObject(buffered));
            await db.HashSetAsync(LlmAgentRedisKeys.AgentChatBatchMeta(message.ChatId), [
                new HashEntry("dueAt", dueAt),
                new HashEntry("botName", botName),
                new HashEntry("botUserId", botUserId),
                new HashEntry("lastMessageId", message.MessageId),
                new HashEntry("updatedAtUtc", DateTime.UtcNow.ToString("O"))
            ]);
            await db.SortedSetAddAsync(LlmAgentRedisKeys.AgentChatBatchDueSet, chatIdMember, dueAt);
            await db.KeyExpireAsync(LlmAgentRedisKeys.AgentChatBatchList(message.ChatId), BatchTtl);
            await db.KeyExpireAsync(LlmAgentRedisKeys.AgentChatBatchMeta(message.ChatId), BatchTtl);
        }
    }
}
