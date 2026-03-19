using System;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class TodoToolService : IService {
        public string ServiceName => "TodoToolService";

        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        public TodoToolService(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        [BuiltInTool("Creates and sends a todo/reminder item to the current chat with structured format.", Name = "send_todo_to_group")]
        public async Task<TodoItemResult> SendTodoToGroup(
            [BuiltInParameter("The todo item title (short summary).", IsRequired = true)] string title,
            [BuiltInParameter("Detailed description of the todo item.", IsRequired = false)] string description,
            [BuiltInParameter("Priority level: low, medium, high, urgent.", IsRequired = false)] string priority,
            [BuiltInParameter("Due date/time in human-readable format (e.g., 'tomorrow', '2025-12-31').", IsRequired = false)] string dueDate,
            ToolContext toolContext,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] int? replyToMessageId = null) {
            try {
                if (string.IsNullOrWhiteSpace(title)) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "Title is required for todo items."
                    };
                }

                var message = FormatTodoMessage(title, description, priority, dueDate);

                var replyParameters = replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null;

                var sentMessage = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                    chatId: toolContext.ChatId,
                    text: message,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters
                ), toolContext.ChatId);

                return new TodoItemResult {
                    Success = true,
                    MessageId = sentMessage.MessageId,
                    ChatId = sentMessage.Chat.Id
                };
            } catch (Exception ex) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send todo: {ex.Message}"
                };
            }
        }

        [BuiltInTool("Sends a quick simple todo message to the current chat.", Name = "send_quick_todo")]
        public async Task<TodoItemResult> SendQuickTodo(
            [BuiltInParameter("The todo message content.", IsRequired = true)] string message,
            ToolContext toolContext,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] int? replyToMessageId = null) {
            try {
                if (string.IsNullOrWhiteSpace(message)) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "Message content is required."
                    };
                }

                var formattedMessage = $"📋 <b>待办</b>\n\n{message}";

                var replyParameters = replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null;

                var sentMessage = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                    chatId: toolContext.ChatId,
                    text: formattedMessage,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters
                ), toolContext.ChatId);

                return new TodoItemResult {
                    Success = true,
                    MessageId = sentMessage.MessageId,
                    ChatId = sentMessage.Chat.Id
                };
            } catch (Exception ex) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send todo: {ex.Message}"
                };
            }
        }

        private string FormatTodoMessage(string title, string description, string priority, string dueDate) {
            var sb = new StringBuilder();
            sb.AppendLine("📋 <b>待办事项</b>");
            sb.AppendLine();
            sb.AppendLine($"📌 <b>标题:</b> {title}");

            if (!string.IsNullOrWhiteSpace(description)) {
                sb.AppendLine($"📝 <b>描述:</b> {description}");
            }

            if (!string.IsNullOrWhiteSpace(dueDate)) {
                sb.AppendLine($"⏰ <b>截止日期:</b> {dueDate}");
            }

            if (!string.IsNullOrWhiteSpace(priority)) {
                var priorityIcon = priority.ToLower() switch {
                    "low" => "🟢",
                    "medium" => "🟡",
                    "high" => "🟠",
                    "urgent" => "🔴",
                    _ => "⚪"
                };
                sb.AppendLine($"🔥 <b>优先级:</b> {priorityIcon} {priority}");
            }

            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"创建时间: {DateTime.Now:yyyy-MM-dd HH:mm}");

            return sb.ToString();
        }
    }
}
