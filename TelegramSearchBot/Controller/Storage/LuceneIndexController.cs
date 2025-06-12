using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Controller.AI.ASR;
using TelegramSearchBot.Controller.AI.LLM;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.AI.QR;
using TelegramSearchBot.Controller.Bilibili;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Help;
using TelegramSearchBot.Controller.Manage;
using TelegramSearchBot.Controller.Search;
using TelegramSearchBot.Interface.Controller;

namespace TelegramSearchBot.Controller.Storage {
    public class LuceneIndexController : IOnUpdate
    {
        private readonly MessageService _messageService;
        public List<Type> Dependencies => new List<Type> { 
            typeof(MessageController),
            typeof(AutoASRController),
            typeof(AltPhotoController),
            typeof(GeneralLLMController),
            typeof(AutoOCRController),
            typeof(AutoQRController),
            typeof(BiliMessageController),
            typeof(DownloadAudioController),
            typeof(DownloadPhotoController),
            typeof(DownloadVideoController),
            typeof(HelpController),
            typeof(AdminController),
            typeof(CheckBanGroupController),
            typeof(EditLLMConfController),
            typeof(RefreshController),
            typeof(SearchController),
            typeof(SearchNextPageController)
        };

        public LuceneIndexController(MessageService messageService)
        {
            _messageService = messageService;
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            if (p.Update.Message == null) return;

            var messageOption = new MessageOption
            {
                MessageDataId = p.MessageDataId,
                ChatId = p.Update.Message.Chat.Id,
                MessageId = p.Update.Message.MessageId,
                UserId = p.Update.Message.From.Id,
                Content = p.Update.Message.Text ?? p.Update.Message.Caption ?? string.Empty,
                DateTime = p.Update.Message.Date,
                User = p.Update.Message.From,
                ReplyTo = p.Update.Message.ReplyToMessage?.Id ?? 0,
                Chat = p.Update.Message.Chat
            };

            await _messageService.AddToLucene(messageOption);
        }
    }
}
