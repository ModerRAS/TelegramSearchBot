#if WINDOWS
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AppUpdate;

namespace TelegramSearchBot.Controller.Manage {
    public class UpdateController : IOnUpdate {
        private readonly ITelegramBotClient _botClient;
        private readonly IHostApplicationLifetime _applicationLifetime;

        public List<Type> Dependencies => new List<Type>() { typeof(AdminController) };

        public UpdateController(
            ITelegramBotClient botClient,
            IHostApplicationLifetime applicationLifetime) {
            _botClient = botClient;
            _applicationLifetime = applicationLifetime;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var message = p.Update.Message;
            if (message?.Text == null) {
                return;
            }

            var text = message.Text.Trim();
            if (!IsUpdateCommand(text)) {
                return;
            }

            // 仅允许全局管理员使用更新指令
            if (message.From.Id != Env.AdminId) {
                await ReplyAsync(message, "❌ 权限不足：只有全局管理员才能使用更新指令。");
                return;
            }

            switch (text.ToLowerInvariant()) {
                case "/checkupdate":
                case "/检查更新":
                case "检查更新":
                    await HandleCheckUpdateAsync(message);
                    break;
                case "/update":
                case "/更新":
                case "更新":
                case "执行更新":
                    await HandleStartUpdateAsync(message);
                    break;
            }
        }

        private static bool IsUpdateCommand(string text) {
            return text.Equals("/checkupdate", StringComparison.OrdinalIgnoreCase)
                || text.Equals("/检查更新", StringComparison.OrdinalIgnoreCase)
                || text.Equals("检查更新", StringComparison.OrdinalIgnoreCase)
                || text.Equals("/update", StringComparison.OrdinalIgnoreCase)
                || text.Equals("/更新", StringComparison.OrdinalIgnoreCase)
                || text.Equals("更新", StringComparison.OrdinalIgnoreCase)
                || text.Equals("执行更新", StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleCheckUpdateAsync(Message message) {
            try {
                var result = await SelfUpdateBootstrap.GetUpdateStatusAsync();
                var sb = new StringBuilder();
                sb.AppendLine("更新状态");
                sb.AppendLine($"当前版本: {result.CurrentVersion}");
                sb.AppendLine($"运行位置: {( result.RunningManagedInstall ? "独立安装目录" : "桥接启动实例" )}");
                sb.AppendLine($"独立安装目录: {( result.ManagedInstallExists ? "已存在" : "尚未建立" )}");

                if (!string.IsNullOrWhiteSpace(result.LatestVersion)) {
                    sb.AppendLine($"最新版本: {result.LatestVersion}");
                }

                switch (result.State) {
                    case ManagedUpdateState.UpToDate:
                        sb.AppendLine("状态: 已是最新版本。");
                        break;
                    case ManagedUpdateState.UpdateAvailable:
                        sb.AppendLine($"状态: 可更新到 {result.TargetVersion ?? result.LatestVersion}。");
                        sb.AppendLine("发送「更新」或 /update 立即开始升级。");
                        break;
                    case ManagedUpdateState.UpdateUnavailable:
                    case ManagedUpdateState.NoPathFound:
                        sb.AppendLine($"状态: {result.Message ?? "当前没有可用更新路径。"}");
                        break;
                    default:
                        sb.AppendLine($"状态: {result.Message ?? result.State.ToString()}");
                        break;
                }

                await ReplyAsync(message, sb.ToString());
            } catch (Exception ex) {
                await ReplyAsync(message, $"❌ 检查更新时发生异常：{ex.Message}");
            }
        }

        private async Task HandleStartUpdateAsync(Message message) {
            try {
                var result = await SelfUpdateBootstrap.StartUpdateAsync();
                string responseText = result.State switch {
                    ManagedUpdateState.UpdateScheduled => $"✅ 已开始准备更新到 {result.TargetVersion ?? result.LatestVersion}，机器人将退出并在更新后自动重启。",
                    ManagedUpdateState.UpToDate => "✅ 当前已经是最新版本，无需更新。",
                    ManagedUpdateState.UpdateAvailable => $"ℹ️ 已检测到新版本 {result.TargetVersion ?? result.LatestVersion}，但暂未成功调度更新。",
                    _ => $"❌ 无法启动更新：{result.Message ?? result.State.ToString()}"
                };

                await ReplyAsync(message, responseText);

                if (result.ShouldStopApplication) {
                    _applicationLifetime.StopApplication();
                }
            } catch (Exception ex) {
                await ReplyAsync(message, $"❌ 启动更新时发生异常：{ex.Message}");
            }
        }

        private async Task ReplyAsync(Message message, string text) {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: text,
                replyParameters: new Telegram.Bot.Types.ReplyParameters {
                    MessageId = message.MessageId
                });
        }
    }
}
#endif
