using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Controller.AI.LLM {
    public class AgentMonitorController : IOnUpdate {
        private readonly AdminService _adminService;
        private readonly AgentRegistryService _agentRegistryService;
        private readonly IConnectionMultiplexer _redis;
        private readonly ISendMessageService _sendMessageService;

        public AgentMonitorController(
            AdminService adminService,
            AgentRegistryService agentRegistryService,
            IConnectionMultiplexer redis,
            ISendMessageService sendMessageService) {
            _adminService = adminService;
            _agentRegistryService = agentRegistryService;
            _redis = redis;
            _sendMessageService = sendMessageService;
        }

        public List<Type> Dependencies => new();

        public async Task ExecuteAsync(PipelineContext p) {
            var message = p.Update?.Message;
            var text = message?.Text;
            if (message?.From == null || string.IsNullOrWhiteSpace(text) || !text.StartsWith("/agent", StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            if (!await _adminService.IsNormalAdmin(message.From.Id) && !_adminService.IsGlobalAdmin(message.From.Id)) {
                return;
            }

            if (text.Equals("/agent list", StringComparison.OrdinalIgnoreCase)) {
                var sessions = await _agentRegistryService.ListActiveAsync();
                if (!sessions.Any()) {
                    await _sendMessageService.SendMessage("当前没有活跃的 LLM Agent。", message.Chat.Id, message.MessageId);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("活跃 LLM Agent：");
                foreach (var session in sessions) {
                    sb.AppendLine($"- ChatId={session.ChatId}, PID={session.ProcessId}, Status={session.Status}, LastHeartbeat={session.LastHeartbeatUtc:O}");
                }

                await _sendMessageService.SendMessage(sb.ToString(), message.Chat.Id, message.MessageId);
                return;
            }

            if (text.Equals("/agent stats", StringComparison.OrdinalIgnoreCase)) {
                var db = _redis.GetDatabase();
                var pending = await db.ListLengthAsync(TelegramSearchBot.Model.AI.LlmAgentRedisKeys.AgentTaskQueue);
                var telegramTasks = await db.ListLengthAsync(TelegramSearchBot.Model.AI.LlmAgentRedisKeys.TelegramTaskQueue);
                var deadLetter = await db.ListLengthAsync(TelegramSearchBot.Model.AI.LlmAgentRedisKeys.AgentTaskDeadLetterQueue);
                var sessions = await _agentRegistryService.ListActiveAsync();
                var processing = sessions.Count(x => !string.IsNullOrWhiteSpace(x.CurrentTaskId));
                var stats = $"Agents={sessions.Count}\nProcessingAgents={processing}\nPendingAgentTasks={pending}\nPendingTelegramTasks={telegramTasks}\nDeadLetterTasks={deadLetter}";
                await _sendMessageService.SendMessage(stats, message.Chat.Id, message.MessageId);
                return;
            }

            if (text.StartsWith("/agent kill ", StringComparison.OrdinalIgnoreCase)) {
                var suffix = text.Substring("/agent kill ".Length).Trim();
                if (!long.TryParse(suffix, out var chatId)) {
                    await _sendMessageService.SendMessage("用法：/agent kill <chatId>", message.Chat.Id, message.MessageId);
                    return;
                }

                var killed = await _agentRegistryService.TryKillAsync(chatId);
                await _sendMessageService.SendMessage(
                    killed ? $"已终止 chatId={chatId} 的 Agent。" : $"无法终止 chatId={chatId} 的 Agent（可能不存在或仍在处理任务）。",
                    message.Chat.Id,
                    message.MessageId);
            }
        }
    }
}
