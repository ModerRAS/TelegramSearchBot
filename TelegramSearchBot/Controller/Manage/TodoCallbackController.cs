using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Todo;

namespace TelegramSearchBot.Controller.Manage {
    public class TodoCallbackController : IOnUpdate {
        private const string CompletePrefix = "todo_complete:";

        private readonly ITelegramBotClient _botClient;
        private readonly TodoService _todoService;
        private readonly ILogger<TodoCallbackController> _logger;

        public TodoCallbackController(
            ITelegramBotClient botClient,
            TodoService todoService,
            ILogger<TodoCallbackController> logger) {
            _botClient = botClient;
            _todoService = todoService;
            _logger = logger;
        }

        public List<Type> Dependencies => new();

        public async Task ExecuteAsync(PipelineContext p) {
            var callbackQuery = p.Update.CallbackQuery;
            if (callbackQuery == null || string.IsNullOrWhiteSpace(callbackQuery.Data)) {
                return;
            }

            if (!callbackQuery.Data.StartsWith(CompletePrefix, StringComparison.Ordinal)) {
                return;
            }

            if (!long.TryParse(callbackQuery.Data.Substring(CompletePrefix.Length), out var todoId)) {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "无效的待办编号");
                return;
            }

            var chatId = callbackQuery.Message?.Chat.Id ?? 0;
            if (chatId == 0) {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "无法确认当前聊天");
                return;
            }

            var result = await _todoService.CompleteTodoAsync(chatId, todoId, callbackQuery.From.Id);
            if (!result.Success) {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, result.Error);
                return;
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "已标记完成");

            if (callbackQuery.Message == null) {
                return;
            }

            try {
                await _botClient.EditMessageReplyMarkup(
                    chatId: callbackQuery.Message.Chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    replyMarkup: null);
            } catch (Telegram.Bot.Exceptions.ApiRequestException ex) {
                _logger.LogWarning(ex, "Failed to clear todo reminder keyboard for todo {TodoId}", todoId);
            }
        }
    }
}
