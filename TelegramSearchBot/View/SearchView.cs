using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Scriban;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Search;
using TelegramSearchBot.Search.Lucene.Model;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View {
    public class SearchView : IView {
        private readonly SendMessage _sendMessage;
        private readonly ITelegramBotClient _botClient;

        public SearchView(
            SendMessage sendMessage,
            ITelegramBotClient botClient) {
            _sendMessage = sendMessage;
            _botClient = botClient;
        }


        private long ChatId { get; set; }
        private int ReplyToMessageId { get; set; }
        private bool IsGroup { get => ChatId < 0; }
        private List<Message> Messages { get; set; }
        private int Count { get; set; }
        private int Skip { get; set; }
        private int Take { get; set; }
        private string Keyword { get; set; }
        private SearchType SearchType { get; set; } = SearchType.InvertedIndex;
        private SearchMessageVO SearchMessageResult { get; set; }
        private List<Button> Buttons { get; set; } = new List<Button>();
        public class Button {
            public string Text { get; set; }
            public string CallbackData { get; set; }
            public Button(string text, string callbackData) {
                Text = text;
                CallbackData = callbackData;
            }
        }

        public SearchView WithChatId(long chatId) {
            ChatId = chatId;
            return this;
        }

        public SearchView WithReplyTo(int messageId) {
            ReplyToMessageId = messageId;
            return this;
        }

        [Obsolete("Use WithSearchResult(SearchMessageVO) instead")]
        public SearchView WithMessages(List<Message> messages) {
            Messages = messages;
            return this;
        }

        public SearchView WithSearchResult(SearchMessageVO searchMessageVO) {
            if (searchMessageVO == null) {
                throw new ArgumentNullException(nameof(searchMessageVO));
            }

            searchMessageVO.Messages ??= new List<MessageVO>();
            SearchMessageResult = searchMessageVO;
            ChatId = searchMessageVO.ChatId;
            Count = searchMessageVO.Count;
            Skip = searchMessageVO.Skip;
            Take = searchMessageVO.Take;
            SearchType = searchMessageVO.SearchType;
            return this;
        }

        public SearchView WithCount(int count) {
            Count = count;
            return this;
        }

        public SearchView WithSkip(int skip) {
            Skip = skip;
            return this;
        }

        public SearchView WithTake(int take) {
            Take = take;
            return this;
        }

        public SearchView WithSearchType(SearchType searchType) {
            SearchType = searchType;
            return this;
        }

        public SearchView WithKeyword(string keyword) {
            Keyword = keyword;
            return this;
        }

        public SearchView AddButton(string text, string callbackData) {
            Buttons.Add(new Button(text, callbackData));
            return this;
        }

        public async Task Render() {
            var searchMessageVO = SearchMessageResult ?? new SearchMessageVO {
                ChatId = this.ChatId,
                Count = this.Count,
                Skip = this.Skip,
                Take = this.Take,
                SearchType = this.SearchType,
                Messages = this.Messages?.Select(message => new MessageVO(message, this.Keyword)).ToList() ?? new List<MessageVO>()
            };

            searchMessageVO.Messages ??= new List<MessageVO>();

            var messageText = RenderSearchResults(searchMessageVO);

            var replyParameters = new Telegram.Bot.Types.ReplyParameters {
                MessageId = this.ReplyToMessageId
            };

            var inlineButtons = this.Buttons?.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null ?
                new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.AddTask(async () => {
                await _botClient.SendMessage(
                chatId: this.ChatId,
                text: messageText,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                replyParameters: replyParameters,
                disableNotification: true,
                replyMarkup: replyMarkup
            );
            }, ChatId < 0);

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

        public string RenderSearchResults(SearchMessageVO searchMessageVO) {
            var template = Template.Parse(SearchResultTemplate);
            return template.Render(new {
                messages = searchMessageVO.Messages,
                search_option = new {
                    count = searchMessageVO.Count,
                    skip = searchMessageVO.Skip,
                    take = searchMessageVO.Take,
                    search_type = ( int ) searchMessageVO.SearchType
                }
            });
        }

        public async Task SendSearchResults(SearchView viewModel) {
            await viewModel.Render();
        }

        [Obsolete("Use RenderSearchResults instead")]
        public List<string> ConvertToMarkdownLinks(IEnumerable<Model.Data.Message> messages) {
            var template = Template.Parse("[{{content | string.truncate 30 | string.replace '\n' '' | string.replace '\r' ''}}](https://t.me/c/{{group_id | string.slice 4}}/{{message_id}})");

            var result = new List<string>();
            foreach (var message in messages) {
                result.Add(template.Render(message));
            }
            return result;
        }
    }
}
