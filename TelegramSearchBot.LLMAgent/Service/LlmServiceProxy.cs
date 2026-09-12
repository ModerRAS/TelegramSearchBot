using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class LlmServiceProxy : IAgentTaskExecutor {
        private readonly IServiceProvider _serviceProvider;
        private readonly LlmChatRunner _chatRunner;
        private readonly ILogger<LlmServiceProxy> _logger;

        public LlmServiceProxy(IServiceProvider serviceProvider, ILogger<LlmServiceProxy> logger) {
            _serviceProvider = serviceProvider;
            _chatRunner = serviceProvider.GetRequiredService<LlmChatRunner>();
            _logger = logger;
        }

        public async IAsyncEnumerable<string> CallAsync(
            AgentExecutionTask task,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) {
            List<AgentHistoryMessage> history = task.History;

            var binding = ToBinding(task.Channel);
            ApplyBotIdentity(task.BotName, task.BotUserId);
            var channel = ToEntity(task.Channel);

            if (task.Kind == AgentTaskKind.Continuation && task.ContinuationSnapshot != null) {
                await foreach (var chunk in _chatRunner.RunFromSnapshotAsync(task.ContinuationSnapshot, channel, binding, executionContext, cancellationToken)
                                   .WithCancellation(cancellationToken)) {
                    yield return chunk;
                }

                yield break;
            }

            var message = new Message {
                Id = -1,
                GroupId = task.ChatId,
                MessageId = task.MessageId,
                FromUserId = task.UserId,
                ReplyToMessageId = 0,
                Content = task.InputMessage,
                DateTime = task.CreatedAtUtc
            };

            var supportsVision = task.Channel.Capabilities.Any(c =>
                c.Name.Equals("vision", StringComparison.OrdinalIgnoreCase) &&
                c.Value.Equals("true", StringComparison.OrdinalIgnoreCase));

            await foreach (var chunk in _chatRunner.RunAsync(history, message, task.ChatId, task.ModelName, channel, binding, executionContext, supportsVision, cancellationToken)
                               .WithCancellation(cancellationToken)) {
                yield return chunk;
            }
        }

        /// <summary>
        /// 从 config 还原 binding（Agent 进程内 transient 实体，不入库）。
        /// 旧 config / legacy 路由无 binding 字段时返回 null，严格走 Provider/Gateway 路径。
        /// </summary>
        private static LLMApiBinding? ToBinding(AgentChannelConfig config) {
            if (!config.BindingId.HasValue || !config.BindingProtocol.HasValue || !config.BindingAuthProfile.HasValue) {
                return null;
            }
            return new LLMApiBinding {
                Id = config.BindingId.Value,
                LLMChannelId = config.ChannelId,
                Endpoint = config.BindingEndpoint,
                Protocol = config.BindingProtocol.Value,
                AuthProfile = config.BindingAuthProfile.Value,
                IsDefault = false
            };
        }

        private void ApplyBotIdentity(string botName, long botUserId) {
            var identityProvider = _serviceProvider.GetService<IBotIdentityProvider>();
            if (identityProvider != null) {
                identityProvider.SetIdentity(botUserId, botName);
            } else {
                _logger.LogDebug(
                    "IBotIdentityProvider is not registered; falling back to Env.BotId for bot {BotName} ({BotUserId}).",
                    botName,
                    botUserId);
                Env.BotId = botUserId;
            }
        }

        private static LLMChannel ToEntity(AgentChannelConfig config) {
            return new LLMChannel {
                Id = config.ChannelId,
                Name = config.Name,
                Gateway = config.Gateway,
                ApiKey = config.ApiKey,
                Parallel = config.Parallel,
                Priority = config.Priority,
                Provider = config.Provider
            };
        }
    }
}
