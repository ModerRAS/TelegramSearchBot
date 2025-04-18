﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoASRController : IOnUpdate {
        private readonly AutoASRService autoASRService;
        private readonly MessageService messageService;
        private readonly ILogger<AutoASRController> logger;

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadAudioController), typeof(DownloadVideoController) };
        public SendMessageService SendMessageService { get; set; }
        public AutoASRController(
            AutoASRService autoASRService, 
            MessageService messageService, 
            ILogger<AutoASRController> logger,
            SendMessageService SendMessageService
            ) {
            this.autoASRService = autoASRService;
            this.messageService = messageService;
            this.logger = logger;
            this.SendMessageService = SendMessageService;
        }
        public async Task<byte[]> GetFileAsync(Update e) {
            if (e?.Message?.Audio is not null || e?.Message?.Voice is not null ||
                (string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessAudio.IsAudio(e?.Message?.Document?.FileName))) {
                return await IProcessAudio.GetAudio(e);
            } else if (e?.Message?.Video is not null ||
                (string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessVideo.IsVideo(e?.Message?.Document?.FileName))) {
                if (Env.EnableVideoASR) {
                    return await IProcessVideo.GetVideo(e);
                }
                throw new FileNotFoundException();
            } else {
                throw new FileNotFoundException();
            }
        }
        public string GetFilePath(Update e) {
            if (e?.Message?.Audio is not null || e?.Message?.Voice is not null ||
                (string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessAudio.IsAudio(e?.Message?.Document?.FileName))) {
                return IProcessAudio.GetAudioPath(e);
            } else if (e?.Message?.Video is not null ||
                (string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessVideo.IsVideo(e?.Message?.Document?.FileName))) {
                if (Env.EnableVideoASR) {
                    return IProcessVideo.GetVideoPath(e);
                }
                throw new FileNotFoundException();
            } else {
                throw new FileNotFoundException();
            }
        }
        public async Task ExecuteAsync(Update e) {
            if (!Env.EnableAutoASR) {
                return;
            }

            try {
                //var AudioStream = await IProcessAudio.ConvertToWav(await GetFileAsync(e));
                //logger.LogInformation($"Get Audio File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var path = GetFilePath(e);
                var AsrStr = await autoASRService.ExecuteAsync(path);
                logger.LogInformation(AsrStr);
                await messageService.ExecuteAsync(new MessageOption {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Chat = e.Message.Chat,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                    Content = $"{e.Message?.Caption}\n{AsrStr}"
                });
                if (AsrStr.Length > 4095) {
                    await SendMessageService.SendDocument(AsrStr, $"{e.Message.MessageId}.srt", e.Message.Chat.Id, e.Message.MessageId);
                } else {
                    await SendMessageService.SendMessage(AsrStr, e.Message.Chat, e.Message.MessageId);
                }
                
            } catch (Exception ex) when (
                  ex is CannotGetAudioException ||
                  ex is CannotGetVideoException ||
                  ex is FileNotFoundException ||
                  ex is DirectoryNotFoundException
                  ) {
                //logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            } 
        }
    }
}
