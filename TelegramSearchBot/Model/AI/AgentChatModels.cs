using System;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Data;
using TelegramChat = Telegram.Bot.Types.Chat;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace TelegramSearchBot.Model.AI {
    public sealed class AgentChatMessageInput {
        public long ChatId { get; set; }
        public ChatType ChatType { get; set; }
        public string ChatTitle { get; set; } = string.Empty;
        public string ChatUsername { get; set; } = string.Empty;
        public long UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsBot { get; set; }
        public int MessageId { get; set; }
        public DateTime DateTime { get; set; }
        public string Content { get; set; } = string.Empty;

        public static AgentChatMessageInput FromTelegramMessage(TelegramMessage message, string content) {
            return new AgentChatMessageInput {
                ChatId = message.Chat.Id,
                ChatType = message.Chat.Type,
                ChatTitle = message.Chat.Title ?? string.Empty,
                ChatUsername = message.Chat.Username ?? string.Empty,
                UserId = message.From?.Id ?? 0,
                FirstName = message.From?.FirstName ?? string.Empty,
                LastName = message.From?.LastName ?? string.Empty,
                Username = message.From?.Username ?? string.Empty,
                IsBot = message.From?.IsBot ?? false,
                MessageId = message.MessageId,
                DateTime = message.Date,
                Content = content
            };
        }

        public TelegramChat ToTelegramChat() {
            return new TelegramChat {
                Id = ChatId,
                Type = ChatType,
                Title = string.IsNullOrWhiteSpace(ChatTitle) ? null : ChatTitle,
                Username = string.IsNullOrWhiteSpace(ChatUsername) ? null : ChatUsername
            };
        }
    }

    public sealed class AgentChatExecutionRequest {
        public AgentChatMessageInput ReplyTarget { get; set; } = new();
        public string InputMessage { get; set; } = string.Empty;
        public string BotName { get; set; } = string.Empty;
        public long BotUserId { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public GroupAgentChatMode Mode { get; set; } = GroupAgentChatMode.GuidedBatch;
    }

    public sealed class AgentChatBufferedMessage {
        public AgentChatMessageInput Message { get; set; } = new();
        public string BotName { get; set; } = string.Empty;
        public long BotUserId { get; set; }
        public DateTime BufferedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
