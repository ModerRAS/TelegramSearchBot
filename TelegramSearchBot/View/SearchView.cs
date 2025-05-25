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
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View
{
    public class SearchView : IView
    {
        private readonly SendMessage _sendMessage;
        private readonly ITelegramBotClient _botClient;

        public SearchView(
            SendMessage sendMessage,
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

        public SearchView WithChatId(long chatId)
        {
            ChatId = chatId;
            return this;
        }

        public SearchView WithReplyTo(int messageId)
        {
            ReplyToMessageId = messageId;
            return this;
        }

        public SearchView WithMessages(List<Message> messages)
        {
            Messages = messages;
            return this;
        }

        public SearchView WithCount(int count)
        {
            Count = count;
            return this;
        }

        public SearchView WithSkip(int skip)
        {
            Skip = skip;
            return this;
        }

        public SearchView WithTake(int take)
        {
            Take = take;
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
                Take = this.Take
            });

            var replyParameters = new Telegram.Bot.Types.ReplyParameters
            {
                MessageId = this.ReplyToMessageId
            };

            var inlineButtons = this.Buttons?.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null ?
                new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.AddTask(async () => {
                await _botClient.SendMessage(
                chatId: this.ChatId,
                text: messageText,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyParameters: replyParameters,
                disableNotification: true,
                replyMarkup: replyMarkup
            );
            }, ChatId < 0);
            
        }

        private const string SearchResultTemplate = @"
{{- if search_option.count > 0 -}}
共找到 {{search_option.count}} 项结果, 当前为第{{search_option.skip + 1}}项到第{{if (search_option.skip + search_option.take) < search_option.count; search_option.skip + search_option.take; else; search_option.count; end}}项
{{- else -}}
未找到结果。
{{- end -}}

{{ ""\n"" }}
{{- for message in messages -}}
[{{message.content | string.truncate 30 | string.replace '\n' '' | string.replace '\r' ''}}](https://t.me/c/{{message.group_id | string.slice 4}}/{{message.message_id}}){{ ""\n"" }}

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
                    take = searchOption.Take
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