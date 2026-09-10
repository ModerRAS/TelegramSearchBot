using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using DataMessage = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Loads recent chat history as neutral <see cref="AgentHistoryMessage"/> rows (batched
    /// user-name and extension lookups). This is the single DB pipeline for LLM history;
    /// per-provider projections convert rows to their native message types without touching
    /// the database. Previously duplicated (with per-message N+1 lookups) in each provider.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LlmHistoryQueryService : IService {
        private readonly DataDbContext _dbContext;
        private readonly LlmVisibilityService? _llmVisibilityService;

        public string ServiceName => nameof(LlmHistoryQueryService);

        public LlmHistoryQueryService(DataDbContext dbContext, LlmVisibilityService? llmVisibilityService = null) {
            _dbContext = dbContext;
            _llmVisibilityService = llmVisibilityService;
        }

        /// <summary>Rows ordered chronologically; the input message is appended last (when visible),
        /// matching the legacy per-provider GetChatHistory behavior.</summary>
        public async Task<List<AgentHistoryMessage>> LoadAsync(long chatId, DataMessage? inputMessage = null, CancellationToken cancellationToken = default) {
            var messages = await _dbContext.Messages.AsNoTracking()
                .Where(m => m.GroupId == chatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                .OrderBy(m => m.DateTime)
                .ToListAsync(cancellationToken);

            if (messages.Count < 10) {
                messages = await _dbContext.Messages.AsNoTracking()
                    .Where(m => m.GroupId == chatId)
                    .OrderByDescending(m => m.DateTime)
                    .Take(10)
                    .OrderBy(m => m.DateTime)
                    .ToListAsync(cancellationToken);
            }

            if (_llmVisibilityService != null) {
                messages = await _llmVisibilityService.FilterVisibleMessagesAsync(chatId, messages, cancellationToken);
            }

            var users = await ResolveUsersAsync(messages, cancellationToken);
            var extensionsByMessageId = await ResolveExtensionsAsync(messages, cancellationToken);

            if (inputMessage != null &&
                ( _llmVisibilityService == null ||
                  !await _llmVisibilityService.IsUserInvisibleAsync(chatId, inputMessage.FromUserId, cancellationToken) )) {
                messages.Add(inputMessage);
                var inputExtensions = await _dbContext.MessageExtensions.AsNoTracking()
                    .Where(x => x.MessageDataId == inputMessage.Id)
                    .ToListAsync(cancellationToken);
                extensionsByMessageId[inputMessage.Id] = inputExtensions
                    .Select(e => new AgentMessageExtensionSnapshot { Name = e.Name, Value = e.Value })
                    .ToList();
                if (!users.ContainsKey(inputMessage.FromUserId)) {
                    var inputUser = await _dbContext.UserData.AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == inputMessage.FromUserId, cancellationToken);
                    if (inputUser != null) {
                        users[inputMessage.FromUserId] = inputUser;
                    }
                }
            }

            return messages.Select(message => {
                users.TryGetValue(message.FromUserId, out var user);
                extensionsByMessageId.TryGetValue(message.Id, out var messageExtensions);
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

        private async Task<Dictionary<long, UserData>> ResolveUsersAsync(List<DataMessage> messages, CancellationToken cancellationToken) {
            var userIds = messages.Select(x => x.FromUserId).Distinct().ToList();
            if (userIds.Count == 0) {
                return new Dictionary<long, UserData>();
            }
            return await _dbContext.UserData.AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        }

        private async Task<Dictionary<long, List<AgentMessageExtensionSnapshot>>> ResolveExtensionsAsync(List<DataMessage> messages, CancellationToken cancellationToken) {
            var messageIds = messages.Select(x => x.Id).ToList();
            if (messageIds.Count == 0) {
                return new Dictionary<long, List<AgentMessageExtensionSnapshot>>();
            }
            var extensionRecords = await _dbContext.MessageExtensions.AsNoTracking()
                .Where(x => messageIds.Contains(x.MessageDataId))
                .ToListAsync(cancellationToken);
            return extensionRecords
                .GroupBy(x => x.MessageDataId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(e => new AgentMessageExtensionSnapshot {
                        Name = e.Name,
                        Value = e.Value
                    }).ToList());
        }
    }

    /// <summary>
    /// Loads the photo attached to a message from the shared Photos directory as PNG bytes.
    /// Single copy of the loader previously duplicated in every provider (filesystem-based,
    /// so it works identically in the main process and LLMAgent worker processes).
    /// </summary>
    public static class LlmPhotoLoader {
        public static byte[]? TryLoadMessagePhoto(long chatId, long messageId, Microsoft.Extensions.Logging.ILogger? logger = null) {
            try {
                var dirPath = System.IO.Path.Combine(Env.WorkDir, "Photos", $"{chatId}");
                if (!System.IO.Directory.Exists(dirPath)) return null;

                var files = System.IO.Directory.GetFiles(dirPath, $"{messageId}.*");
                if (files.Length == 0) return null;

                using var fileStream = System.IO.File.OpenRead(files[0]);
                var bitmap = SkiaSharp.SKBitmap.Decode(fileStream);
                if (bitmap == null) return null;

                var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                return encoded?.ToArray();
            } catch (Exception ex) {
                logger?.LogDebug(ex, "无法加载消息图片: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
                return null;
            }
        }
    }
}

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>Shared sender-name formatting for history projections.</summary>
    public static class LlmHistoryFormat {
        public static string FormatSender(AgentUserSnapshot user, long fromUserId) {
            if (fromUserId == 0) return "System/Unknown";
            var name = $"{user?.FirstName} {user?.LastName}".Trim();
            return string.IsNullOrEmpty(name) ? $"User({fromUserId})" : name;
        }
    }
}
