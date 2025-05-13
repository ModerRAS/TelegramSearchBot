using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.ASR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Handler.AI.ASR
{
    public class AutoAsrRequest : IRequest<Unit>
    {
        public Update Update { get; }
        public AutoAsrRequest(Update update)
        {
            Update = update;
        }
    }

    public class AutoAsrRequestHandler : IRequestHandler<AutoAsrRequest, Unit>
    {
        private readonly AutoASRService _autoAsrService;
        private readonly MessageService _messageService;
        private readonly ILogger<AutoAsrRequestHandler> _logger;
        private readonly SendMessageService _sendMessageService;

        public AutoAsrRequestHandler(
            AutoASRService autoAsrService,
            MessageService messageService,
            ILogger<AutoAsrRequestHandler> logger,
            SendMessageService sendMessageService)
        {
            _autoAsrService = autoAsrService;
            _messageService = messageService;
            _logger = logger;
            _sendMessageService = sendMessageService;
        }

        private string GetFilePath(Update e)
        {
            if (e?.Message?.Audio is not null || e?.Message?.Voice is not null ||
                string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessAudio.IsAudio(e?.Message?.Document?.FileName))
            {
                return IProcessAudio.GetAudioPath(e);
            }
            else if (e?.Message?.Video is not null ||
                string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessVideo.IsVideo(e?.Message?.Document?.FileName))
            {
                if (Env.EnableVideoASR)
                {
                    return IProcessVideo.GetVideoPath(e);
                }
                throw new FileNotFoundException();
            }
            else
            {
                throw new FileNotFoundException();
            }
        }

        public async Task<Unit> Handle(AutoAsrRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            if (!Env.EnableAutoASR)
            {
                return Unit.Value;
            }
            try
            {
                var path = GetFilePath(e);
                var asrStr = await _autoAsrService.ExecuteAsync(path);
                _logger.LogInformation(asrStr);
                await _messageService.ExecuteAsync(new MessageOption
                {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Chat = e.Message.Chat,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                    Content = $"{e.Message?.Caption}\n{asrStr}"
                });
                if (asrStr.Length > 4095)
                {
                    await _sendMessageService.SendDocument(asrStr, $"{e.Message.MessageId}.srt", e.Message.Chat.Id, e.Message.MessageId);
                }
                else
                {
                    await _sendMessageService.SendMessage(asrStr, e.Message.Chat, e.Message.MessageId);
                }
            }
            catch (Exception ex) when (
                  ex is CannotGetAudioException ||
                  ex is CannotGetVideoException ||
                  ex is FileNotFoundException ||
                  ex is DirectoryNotFoundException
                  )
            {
                // _logger.LogInformation($"Cannot Get Audio/Video: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }
            return Unit.Value;
        }
    }
} 