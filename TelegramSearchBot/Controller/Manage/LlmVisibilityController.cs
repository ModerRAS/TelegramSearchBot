using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Controller.Storage;

namespace TelegramSearchBot.Controller.Manage {
    public class LlmVisibilityController : IOnUpdate {
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly ISendMessageService _sendMessageService;

        public LlmVisibilityController(
            LlmVisibilityService llmVisibilityService,
            ISendMessageService sendMessageService) {
            _llmVisibilityService = llmVisibilityService;
            _sendMessageService = sendMessageService;
        }

        public List<Type> Dependencies => new List<Type> { typeof(MessageController) };

        public async Task ExecuteAsync(PipelineContext p) {
            var message = p.Update.Message;
            if (message == null || message.Chat.Id >= 0 || message.From == null) {
                return;
            }

            var command = NormalizeCommand(message.Text ?? message.Caption ?? string.Empty);
            if (string.IsNullOrWhiteSpace(command)) {
                return;
            }

            if (IsEnableCommand(command)) {
                await _llmVisibilityService.SetUserInvisibleAsync(message.Chat.Id, message.From.Id, true);
                await _sendMessageService.SendMessage("已开启 LLM 隐身。之后你的群消息、图片 Alt 和消息扩展信息都不会发送给 LLM。", message.Chat.Id, message.MessageId);
                return;
            }

            if (IsDisableCommand(command)) {
                await _llmVisibilityService.SetUserInvisibleAsync(message.Chat.Id, message.From.Id, false);
                await _sendMessageService.SendMessage("已关闭 LLM 隐身。之后你的群消息可以进入 LLM 上下文。", message.Chat.Id, message.MessageId);
                return;
            }

            if (IsStatusCommand(command)) {
                var enabled = await _llmVisibilityService.IsUserInvisibleAsync(message.Chat.Id, message.From.Id);
                var status = enabled ? "已开启" : "未开启";
                await _sendMessageService.SendMessage($"LLM 隐身状态：{status}", message.Chat.Id, message.MessageId);
            }
        }

        private static string NormalizeCommand(string rawCommand) {
            var command = rawCommand.Trim();
            if (string.IsNullOrWhiteSpace(command)) {
                return string.Empty;
            }

            var tokenEnd = command.IndexOfAny(new[] { ' ', '\r', '\n', '\t' });
            if (tokenEnd >= 0) {
                command = command.Substring(0, tokenEnd);
            }

            var botSuffixStart = command.IndexOf('@');
            if (botSuffixStart >= 0) {
                command = command.Substring(0, botSuffixStart);
            }

            return command.Trim();
        }

        private static bool IsEnableCommand(string command) {
            return command.Equals("LLM隐身", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("/llm_invisible_on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisableCommand(string command) {
            return command.Equals("取消LLM隐身", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("LLM显身", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("/llm_invisible_off", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStatusCommand(string command) {
            return command.Equals("LLM隐身状态", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("/llm_invisible_status", StringComparison.OrdinalIgnoreCase);
        }
    }
}
