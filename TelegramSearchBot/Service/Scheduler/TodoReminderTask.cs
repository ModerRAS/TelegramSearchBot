using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Todo;

namespace TelegramSearchBot.Service.Scheduler {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class TodoReminderTask : IScheduledTask {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<TodoReminderTask> _logger;
        private Func<Task> _heartbeatCallback;

        public TodoReminderTask(
            IServiceProvider serviceProvider,
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ILogger<TodoReminderTask> logger) {
            _serviceProvider = serviceProvider;
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        public string TaskName => "TodoReminder";

        public string CronExpression => "* * * * *";

        public void SetHeartbeatCallback(Func<Task> heartbeatCallback) {
            _heartbeatCallback = heartbeatCallback;
        }

        public async Task ExecuteAsync() {
            using var scope = _serviceProvider.CreateScope();
            var todoService = scope.ServiceProvider.GetRequiredService<TodoService>();
            var dueTodos = await todoService.GetPendingRemindersAsync(DateTime.UtcNow);

            foreach (var todo in dueTodos) {
                if (_heartbeatCallback != null) {
                    await _heartbeatCallback();
                }

                var reminderText = todoService.BuildReminderMessage(todo);
                var keyboard = new InlineKeyboardMarkup(new[] {
                    new[] {
                        InlineKeyboardButton.WithCallbackData("✅ 标记完成", $"todo_complete:{todo.Id}")
                    }
                });

                var sentMessage = await _sendMessage.AddTaskWithResult(
                    async () => await _botClient.SendMessage(
                        chatId: todo.ChatId,
                        text: reminderText,
                        parseMode: ParseMode.Html,
                        replyMarkup: keyboard),
                    todo.ChatId);

                await todoService.MarkReminderSentAsync(todo.Id, sentMessage.MessageId, DateTime.UtcNow);
                _logger.LogInformation("Sent reminder for todo {TodoId} to chat {ChatId}", todo.Id, todo.ChatId);
            }
        }
    }
}
