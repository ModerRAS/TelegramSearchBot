using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.View
{
    public class EditLLMConfView : IView
    {
        private readonly ISendMessageService _sendMessageService;

        private long _chatId;
        private int _replyToMessageId;
        private string _messageText;

        public EditLLMConfView(ISendMessageService sendMessageService)
        {
            _sendMessageService = sendMessageService;
        }

        public IView WithChatId(long chatId)
        {
            _chatId = chatId;
            return this;
        }

        public IView WithReplyTo(int messageId)
        {
            _replyToMessageId = messageId;
            return this;
        }

        public IView WithText(string text)
        {
            _messageText = text;
            return this;
        }

        public IView WithCount(int count)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSkip(int skip)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTake(int take)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSearchType(SearchType searchType)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessages(List<Message> messages)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTitle(string title)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp()
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView DisableNotification(bool disable = true)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessage(string message)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName)
        {
            // EditLLMConfView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public async Task Render()
        {
            await _sendMessageService.SplitAndSendTextMessage(
                _messageText,
                _chatId,
                _replyToMessageId);
        }
    }
}