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
        private readonly ILogger<LlmServiceProxy> _logger;

        public LlmServiceProxy(IServiceProvider serviceProvider, ILogger<LlmServiceProxy> logger) {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async IAsyncEnumerable<string> CallAsync(
            AgentExecutionTask task,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) {
            await SeedTaskDataAsync(task, cancellationToken);

            var service = ResolveService(task.Channel.Provider);
            ApplyBotIdentity(task.BotName, task.BotUserId);
            var channel = ToEntity(task.Channel);

            if (task.Kind == AgentTaskKind.Continuation && task.ContinuationSnapshot != null) {
                await foreach (var chunk in service.ResumeFromSnapshotAsync(task.ContinuationSnapshot, channel, executionContext, cancellationToken)
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

            await foreach (var chunk in service.ExecAsync(message, task.ChatId, task.ModelName, channel, executionContext, cancellationToken)
                               .WithCancellation(cancellationToken)) {
                yield return chunk;
            }
        }

        private ILLMService ResolveService(LLMProvider provider) {
            return provider switch {
                LLMProvider.Ollama => _serviceProvider.GetRequiredService<OllamaService>(),
                LLMProvider.Gemini => _serviceProvider.GetRequiredService<GeminiService>(),
                LLMProvider.Anthropic => _serviceProvider.GetRequiredService<AnthropicService>(),
                LLMProvider.ResponsesAPI => _serviceProvider.GetRequiredService<OpenAIResponsesService>(),
                _ => _serviceProvider.GetRequiredService<OpenAIService>()
            };
        }

        private void ApplyBotIdentity(string botName, long botUserId) {
            var identityProvider = _serviceProvider.GetService<IBotIdentityProvider>();
            if (identityProvider != null) {
                identityProvider.SetIdentity(botUserId, botName);
            } else {
                Env.BotId = botUserId;
            }
        }

        private async Task SeedTaskDataAsync(AgentExecutionTask task, CancellationToken cancellationToken) {
            var dbContext = _serviceProvider.GetRequiredService<DataDbContext>();
            await dbContext.Database.EnsureDeletedAsync(cancellationToken);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);

            dbContext.LLMChannels.Add(ToEntity(task.Channel));
            var channelWithModel = new ChannelWithModel {
                Id = 1,
                LLMChannelId = task.Channel.ChannelId,
                ModelName = task.ModelName,
                IsDeleted = false
            };
            dbContext.ChannelsWithModel.Add(channelWithModel);
            dbContext.GroupSettings.Add(new GroupSettings {
                GroupId = task.ChatId,
                LLMModelName = task.ModelName
            });

            foreach (var capability in task.Channel.Capabilities) {
                dbContext.ModelCapabilities.Add(new ModelCapability {
                    ChannelWithModelId = channelWithModel.Id,
                    CapabilityName = capability.Name,
                    CapabilityValue = capability.Value,
                    Description = capability.Description
                });
            }

            var seededUsers = new HashSet<long>();
            foreach (var historyMessage in task.History) {
                dbContext.Messages.Add(new Message {
                    Id = historyMessage.DataId,
                    DateTime = historyMessage.DateTime,
                    GroupId = historyMessage.GroupId,
                    MessageId = historyMessage.MessageId,
                    FromUserId = historyMessage.FromUserId,
                    ReplyToUserId = historyMessage.ReplyToUserId,
                    ReplyToMessageId = historyMessage.ReplyToMessageId,
                    Content = historyMessage.Content
                });

                if (seededUsers.Add(historyMessage.User.UserId)) {
                    dbContext.UserData.Add(new UserData {
                        Id = historyMessage.User.UserId,
                        FirstName = historyMessage.User.FirstName,
                        LastName = historyMessage.User.LastName,
                        UserName = historyMessage.User.UserName,
                        IsBot = historyMessage.User.IsBot,
                        IsPremium = historyMessage.User.IsPremium
                    });
                }

                foreach (var extension in historyMessage.Extensions) {
                    dbContext.MessageExtensions.Add(new MessageExtension {
                        MessageDataId = historyMessage.DataId,
                        Name = extension.Name,
                        Value = extension.Value
                    });
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
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
