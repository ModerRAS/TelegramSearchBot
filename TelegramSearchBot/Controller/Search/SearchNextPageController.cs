using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using TelegramSearchBot.Intrerface;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using LiteDB;
using Telegram.Bot.Types;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Search;
using MediatR;
using System.Threading;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.Controller.Search
{
    class SearchNextPageController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly SendMessage _sendManager; // Renamed Send to _sendManager to avoid conflict with keyword
        private readonly ILiteCollection<CacheData> _cache;
        private readonly ILogger<SearchNextPageController> _logger;
        private readonly ISearchService _searchService;
        private readonly SendService _sendService;
        private readonly ITelegramBotClient _botClient;
        // public List<Type> Dependencies => new List<Type>(); // Obsolete with MediatR

        public SearchNextPageController(
            ITelegramBotClient botClient,
            SendMessage sendManager, // Renamed Send to sendManager
            ILogger<SearchNextPageController> logger,
            SearchService searchService, // Assuming SearchService implements ISearchService
            SendService sendService
            )
        {
            _sendService = sendService;
            _searchService = searchService;
            _sendManager = sendManager;
            _cache = Env.Cache.GetCollection<CacheData>("CacheData");
            _logger = logger;
            _botClient = botClient;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            if (update.Type != UpdateType.CallbackQuery || update.CallbackQuery == null)
            {
                return;
            }

            var callbackQuery = update.CallbackQuery;
            var chatId = callbackQuery.Message?.Chat?.Id; // Null-conditional access

            if (chatId == null)
            {
                _logger.LogWarning("CallbackQuery received without ChatId. CallbackQueryId: {CallbackQueryId}", callbackQuery.Id);
                return;
            }
            
            _logger.LogInformation("Handling CallbackQuery: {CallbackQueryData} for ChatId: {ChatId}", callbackQuery.Data, chatId);
            
            bool isGroup = callbackQuery.Message.Chat.Type != ChatType.Private; // More robust check for group

            try
            {
                // Answer callback query immediately to provide feedback to the user.
                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "处理中...", cancellationToken: cancellationToken);

                var cacheData = _cache.Find(c => c.UUID.Equals(callbackQuery.Data)).FirstOrDefault();
                if (cacheData == null || cacheData.searchOption == null)
                {
                    _logger.LogWarning("CacheData not found or searchOption is null for CallbackQueryData: {CallbackQueryData}", callbackQuery.Data);
                    // Optionally send a message to the user that the action has expired or is invalid.
                    // await _botClient.SendTextMessageAsync(chatId, "此操作已过期或无效。", cancellationToken: cancellationToken);
                    return;
                }
                
                var searchOption = cacheData.searchOption;
                _cache.Delete(cacheData.Id);

                if (callbackQuery.Message != null) // Ensure message context exists
                {
                    searchOption.ToDelete.Add(callbackQuery.Message.MessageId);
                    searchOption.ReplyToMessageId = callbackQuery.Message.MessageId; // This might be confusing if we delete this message
                    searchOption.Chat = callbackQuery.Message.Chat;
                }


                if (searchOption.ToDeleteNow)
                {
                    foreach (var messageIdToDelete in searchOption.ToDelete)
                    {
                        // Using _sendManager (renamed from Send)
                        await _sendManager.AddTask(async () =>
                        {
                            try
                            {
                                await _botClient.DeleteMessageAsync(chatId, (int)messageIdToDelete, cancellationToken: cancellationToken);
                            }
                            catch (Exception ex) // Catch more specific exceptions if possible
                            {
                                _logger.LogError(ex, "Failed to delete message {MessageIdToDelete} in chat {ChatId}", messageIdToDelete, chatId);
                            }
                        }, isGroup);
                    }
                    return; // Assuming ToDeleteNow means no further search/send
                }

                var searchOptionNext = await _searchService.Search(searchOption);
                await _sendService.ExecuteAsync(searchOption, searchOptionNext.Messages); // Pass CancellationToken if SendService supports it

            }
            catch (KeyNotFoundException)
            {

            }
            catch (ArgumentException)
            {

            }
        }
    }
}
