using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Scriban;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.View
{
    public class SearchView : IView
    {
        private readonly ISendMessageService _sendMessage;
        private readonly ITelegramBotClient _botClient;

        public SearchView(
            ISendMessageService sendMessage,
            ITelegramBotClient botClient)
        {
            _sendMessage = sendMessage;
            _botClient = botClient;
        }


        private long ChatId { get; set; }
        private int ReplyToMessageId { get; set; }
        private bool IsGroup { get => ChatId < 0;}
        private List<Message> Messages { get; set; }
        private int Count { get; set; }
        private int Skip { get; set; }
        private int Take { get; set; }
        private SearchType SearchType { get; set; } = SearchType.InvertedIndex;
        private List<Button> Buttons { get; set; } = new List<Button>();
        public class Button
        {
            public string Text { get; set; }
            public string CallbackData { get; set; }
            public Button(string text, string callbackData)
            {
                Text = text;
                CallbackData = callbackData;
            }
        }

        public IView WithChatId(long chatId)
        {
            ChatId = chatId;
            return this;
        }

        public IView WithReplyTo(int messageId)
        {
            ReplyToMessageId = messageId;
            return this;
        }

        public IView WithText(string text)
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithCount(int count)
        {
            Count = count;
            return this;
        }

        public IView WithSkip(int skip)
        {
            Skip = skip;
            return this;
        }

        public IView WithTake(int take)
        {
            Take = take;
            return this;
        }

        public IView WithSearchType(SearchType searchType)
        {
            SearchType = searchType;
            return this;
        }

        public IView WithMessages(List<Message> messages)
        {
            Messages = messages;
            return this;
        }

        public IView WithTitle(string title)
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp()
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView DisableNotification(bool disable = true)
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessage(string message)
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName)
        {
            // SearchView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public SearchView AddButton(string text, string callbackData)
        {
            Buttons.Add(new Button(text, callbackData));
            return this;
        }

        public async Task Render()
        {
            var messageText = RenderSearchResults(new SearchOption
            {
                ChatId = this.ChatId,
                ReplyToMessageId = this.ReplyToMessageId,
                IsGroup = this.IsGroup,
                Messages = this.Messages,
                Count = this.Count,
                Skip = this.Skip,
                Take = this.Take,
                SearchType = this.SearchType
            });

            var replyParameters = new Telegram.Bot.Types.ReplyParameters
            {
                MessageId = this.ReplyToMessageId
            };

            var inlineButtons = this.Buttons?.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null ?
                new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.SendButtonMessageAsync(messageText, this.ChatId, this.ReplyToMessageId, this.Buttons?.Select(b => (b.Text, b.CallbackData)).ToArray() ?? Array.Empty<(string, string)>());

        }

        private const string SearchResultTemplate = @"
{{- search_type_text = search_option.search_type == 0 ? ""倒排索引"" : search_option.search_type == 1 ? ""向量搜索"" : ""语法搜索"" -}}
<b>搜索方式</b>: {{search_type_text}}

{{- if search_option.count > 0 -}}
共找到 {{search_option.count}} 项结果, 当前为第{{search_option.skip + 1}}项到第{{if (search_option.skip + search_option.take) < search_option.count; search_option.skip + search_option.take; else; search_option.count; end}}项
{{- else -}}
未找到结果。
{{- end -}}

{{ ""\n"" }}
{{- for message in messages -}}
<a href=""https://t.me/c/{{message.group_id | string.slice 4}}/{{message.message_id}}"">{{message.content | string.truncate 30 | string.replace '\n' '' | string.replace '\r' '' | html.escape}}</a>{{ ""\n"" }}

{{- end -}}
";

        public string RenderSearchResults(SearchOption searchOption)
        {
            var template = Template.Parse(SearchResultTemplate);
            return template.Render(new
            {
                messages = searchOption.Messages,
                search_option = new
                {
                    count = searchOption.Count,
                    skip = searchOption.Skip,
                    take = searchOption.Take,
                    search_type = (int)searchOption.SearchType
                }
            });
        }

        public async Task SendSearchResults(SearchView viewModel)
        {
            await viewModel.Render();
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
    }
}