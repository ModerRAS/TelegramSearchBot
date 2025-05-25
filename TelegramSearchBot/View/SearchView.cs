using System.Collections.Generic;
using System.Threading.Tasks;
using Scriban;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View
{
    public class SearchView : IView
    {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ISearchService _searchService;

        public SearchView(
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ISearchService searchService = null)
        {
            _botClient = botClient;
            _sendMessage = sendMessage;
            _searchService = searchService;
        }

        private const string SearchResultTemplate = @"
{{- if search_option.count > 0 -}}
共找到 {{search_option.count}} 项结果, 当前为第{{search_option.skip + 1}}项到第{{if (search_option.skip + search_option.take) < search_option.count; search_option.skip + search_option.take; else; search_option.count; end}}项
{{- else -}}
未找到结果。
{{- end -}}

{{- for message in messages -}}
[{{message.content | string.truncate 30 | string.replace '\n' '' | string.replace '\r' ''}}](https://t.me/c/{{message.group_id | string.slice 4}}/{{message.message_id}})
{{- end -}}
";

        public string RenderSearchResults(SearchOption searchOption)
        {
            var template = Template.Parse(SearchResultTemplate);
            return template.Render(new {
                messages = searchOption.Messages,
                search_option = new {
                    count = searchOption.Count,
                    skip = searchOption.Skip,
                    take = searchOption.Take
                }
            });
        }

        public async Task SendSearchResults(
            ITelegramBotClient botClient,
            SendMessage send,
            SearchOption searchOption,
            List<InlineKeyboardButton> keyboardButtons)
        {
            var messageText = RenderSearchResults(searchOption);
            
            await send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: searchOption.ChatId,
                    disableNotification: true,
                    parseMode: ParseMode.Markdown,
                    replyParameters: new Telegram.Bot.Types.ReplyParameters
                    {
                        MessageId = searchOption.ReplyToMessageId
                    },
                    replyMarkup: new InlineKeyboardMarkup(keyboardButtons),
                    text: messageText
                );
            }, searchOption.IsGroup);
        }

        public List<string> ConvertToMarkdownLinks(IEnumerable<Model.Data.Message> messages)
        {
            var template = Template.Parse("[{{content | string.truncate 30 | string.replace '\n' '' | string.replace '\r' ''}}](https://t.me/c/{{group_id | string.slice 4}}/{{message_id}})");
            
            var result = new List<string>();
            foreach (var message in messages)
            {
                result.Add(template.Render(message));
            }
            return result;
        }

        public List<InlineKeyboardButton> GenerateKeyboardButtons(List<(string Text, string CallbackData)> buttonConfigs)
        {
            var keyboardButtons = new List<InlineKeyboardButton>();
            foreach (var config in buttonConfigs)
            {
                keyboardButtons.Add(InlineKeyboardButton.WithCallbackData(config.Text, config.CallbackData));
            }
            return keyboardButtons;
        }
    }
}