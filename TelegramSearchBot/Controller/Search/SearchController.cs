using System.Collections.Generic;
using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using MediatR;
using System.Threading;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Search;
using TelegramSearchBot.Service.BotAPI;


namespace TelegramSearchBot.Controller.Search
{
    class SearchController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly ISearchService _searchService;
        private readonly SendService _sendService;
        // public List<Type> Dependencies => new List<Type>(); // Obsolete with MediatR

        public SearchController(
            SearchService searchService, // Assuming SearchService implements ISearchService or is the concrete type used
            SendService sendService
            )
        {
            _searchService = searchService;
            _sendService = sendService;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            if (update.Type != UpdateType.Message || update.Message?.Text == null)
            {
                return;
            }

            var message = update.Message;

            if (message.Text.Length >= 4 && message.Text.StartsWith("搜索 "))
            {
                var firstSearch = new SearchOption()
                {
                    Search = message.Text.Substring(3),
                    ChatId = message.Chat.Id,
                    IsGroup = message.Chat.Id < 0, // Consider using message.Chat.Type for more clarity
                    Skip = 0,
                    Take = 20,
                    Count = -1,
                    ToDelete = new List<long>(),
                    ToDeleteNow = false,
                    ReplyToMessageId = message.MessageId,
                    Chat = message.Chat
                };

                var searchOption = await _searchService.Search(firstSearch);

                // Assuming sendService.ExecuteAsync is designed to handle cancellationToken if needed
                await _sendService.ExecuteAsync(searchOption, searchOption.Messages);
            }
        }
    }
}
