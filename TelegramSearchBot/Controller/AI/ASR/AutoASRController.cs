using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Core.Exceptions;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Interface.AI.ASR;
using TelegramSearchBot.Core.Interface.Controller;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Service.AI.ASR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.ASR {
    public class AutoASRController : IOnUpdate {
        private readonly IAutoASRService autoASRService;
        private readonly MessageService messageService;
        private readonly ILogger<AutoASRController> logger;
        private readonly MessageExtensionService MessageExtensionService;

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadAudioController), typeof(DownloadVideoController), typeof(MessageController) };
        public ISendMessageService SendMessageService { get; set; }
        public AutoASRController(
            IAutoASRService autoASRService,
            MessageService messageService,
            ILogger<AutoASRController> logger,
            ISendMessageService SendMessageService,
            MessageExtensionService messageExtensionService
            ) {
            this.autoASRService = autoASRService;
            this.messageService = messageService;
            this.logger = logger;
            this.SendMessageService = SendMessageService;
            MessageExtensionService = messageExtensionService;
        }
        public async Task<byte[]> GetFileAsync(Update e) {
            if (e?.Message?.Audio is not null || e?.Message?.Voice is not null ||
                string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessAudio.IsAudio(e?.Message?.Document?.FileName)) {
                return await IProcessAudio.GetAudio(e);
            } else if (e?.Message?.Video is not null ||
                  string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessVideo.IsVideo(e?.Message?.Document?.FileName)) {
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
                string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessAudio.IsAudio(e?.Message?.Document?.FileName)) {
                return IProcessAudio.GetAudioPath(e);
            } else if (e?.Message?.Video is not null ||
                  string.IsNullOrEmpty(e?.Message?.Document?.FileName) && IProcessVideo.IsVideo(e?.Message?.Document?.FileName)) {
                if (Env.EnableVideoASR) {
                    return IProcessVideo.GetVideoPath(e);
                }
                throw new FileNotFoundException();
            } else {
                throw new FileNotFoundException();
            }
        }
        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (p.BotMessageType != BotMessageType.Message) {
                return;
            }
            if (!Env.EnableAutoASR) {
                return;
            }

            try {
                //var AudioStream = await IProcessAudio.ConvertToWav(await GetFileAsync(e));
                //logger.LogInformation($"Get Audio File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var path = GetFilePath(e);
                var AsrStr = await autoASRService.ExecuteAsync(path);
                logger.LogInformation(AsrStr);
                await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "ASR_Result", AsrStr);
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
