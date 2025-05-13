using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Handler.AI.LLM
{
    public class GeneralLlmRequest : IRequest<Unit>
    {
        public Update Update { get; }
        public GeneralLlmRequest(Update update)
        {
            Update = update;
        }
    }

    public class GeneralLlmRequestHandler : IRequestHandler<GeneralLlmRequest, Unit>
    {
        private readonly ILogger<GeneralLlmRequestHandler> _logger;
        private readonly OpenAIService _service;
        private readonly SendMessageService _sendMessageService;
        private readonly MessageService _messageService;
        private readonly AdminService _adminService;
        private readonly GeneralLLMService _generalLlmService;
        private readonly ITelegramBotClient _botClient;

        public GeneralLlmRequestHandler(
            MessageService messageService,
            ITelegramBotClient botClient,
            OpenAIService openaiService,
            ILogger<GeneralLlmRequestHandler> logger,
            AdminService adminService,
            SendMessageService sendMessageService,
            GeneralLLMService generalLlmService)
        {
            _logger = logger;
            _botClient = botClient;
            _service = openaiService;
            _sendMessageService = sendMessageService;
            _messageService = messageService;
            _adminService = adminService;
            _generalLlmService = generalLlmService;
        }

        public async Task<Unit> Handle(GeneralLlmRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            if (!Env.EnableOpenAI)
            {
                return Unit.Value;
            }
            if (string.IsNullOrEmpty(_service.BotName))
            {
                var me = await _botClient.GetMe();
                Env.BotId = me.Id;
                _service.BotName = me.Username;
            }

            var Message = string.IsNullOrEmpty(e?.Message?.Text) ? e?.Message?.Caption : e.Message.Text;
            if (string.IsNullOrEmpty(Message))
            {
                return Unit.Value;
            }

            if (e.Message.Entities != null && !string.IsNullOrEmpty(_service.BotName) &&
                e.Message.Entities.Any(entity => entity.Type == MessageEntityType.BotCommand))
            {
                var botCommandEntity = e.Message.Entities.First(entity => entity.Type == MessageEntityType.BotCommand);
                if (botCommandEntity.Offset == 0)
                {
                    string commandText = Message.Substring(botCommandEntity.Offset, botCommandEntity.Length);
                    if (commandText.Contains($"@{_service.BotName}"))
                    {
                        _logger.LogInformation($"Ignoring command '{commandText}' in GeneralLlmRequestHandler as it's a direct command to the bot and should be handled by a dedicated command handler. MessageId: {e.Message.MessageId}");
                        return Unit.Value;
                    }
                }
            }

            if (Message.StartsWith("设置模型 ") && await _adminService.IsNormalAdmin(e.Message.From.Id))
            {
                var (previous, current) = await _service.SetModel(Message.Substring(5), e.Message.Chat.Id);
                _logger.LogInformation($"群{e.Message.Chat.Id}模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{e.Message.MessageId}");
                await _sendMessageService.SendMessage($"模型设置成功，原模型：{previous}，现模型：{current}", e.Message.Chat.Id, e.Message.MessageId);
                return Unit.Value;
            }

            bool isMentionToBot = !string.IsNullOrEmpty(_service.BotName) && Message.Contains($"@{_service.BotName}");
            bool isReplyToBot = e.Message.ReplyToMessage != null && e.Message.ReplyToMessage.From != null && e.Message.ReplyToMessage.From.Id == Env.BotId;

            if (isMentionToBot || isReplyToBot)
            {
                var modelName = await _service.GetModel(e.Message.Chat.Id);
                var initialContentPlaceholder = $"{modelName}初始化中。。。";

                var inputLlMessage = new Model.Data.Message()
                {
                    Content = Message,
                    DateTime = e.Message.Date,
                    FromUserId = e.Message.From.Id,
                    GroupId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    ReplyToMessageId = e.Message.ReplyToMessage?.MessageId ?? 0,
                    Id = -1,
                };

                IAsyncEnumerable<string> fullMessageStream = _generalLlmService.ExecAsync(inputLlMessage, e.Message.Chat.Id, cancellationToken);

                List<Model.Data.Message> sentMessagesForDb = await _sendMessageService.SendFullMessageStream(
                    fullMessageStream,
                    e.Message.Chat.Id,
                    e.Message.MessageId,
                    initialContentPlaceholder,
                    cancellationToken
                );

                User botUser = null;
                foreach (var dbMessage in sentMessagesForDb)
                {
                    if (botUser == null)
                    {
                        botUser = await _botClient.GetMe();
                    }
                    await _messageService.ExecuteAsync(new MessageOption()
                    {
                        Chat = e.Message.Chat,
                        ChatId = dbMessage.GroupId,
                        Content = dbMessage.Content,
                        DateTime = dbMessage.DateTime,
                        MessageId = dbMessage.MessageId,
                        User = botUser,
                        ReplyTo = dbMessage.ReplyToMessageId,
                        UserId = dbMessage.FromUserId,
                    });
                }
            }
            return Unit.Value;
        }
    }
} 