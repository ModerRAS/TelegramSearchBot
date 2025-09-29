using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Manage {
    public class AccountController : IOnUpdate {
        public List<Type> Dependencies => new List<Type>();

        private readonly IAccountService _accountService;
        private readonly ISendMessageService _sendMessageService;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        public AccountController(
            IAccountService accountService,
            ISendMessageService sendMessageService,
            ITelegramBotClient botClient,
            SendMessage sendMessage) {
            _accountService = accountService;
            _sendMessageService = sendMessageService;
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            // 检查账本功能是否启用
            if (!Env.EnableAccounting) {
                return; // 功能未启用，直接返回
            }

            var update = p.Update;
            await ExecuteAsync(update);
            if (update?.Message == null || update.Message.Chat.Id > 0) {
                return; // 只处理群组消息
            }

            var message = update.Message;
            var chatId = message.Chat.Id;
            var userId = message.From.Id;
            var username = message.From.Username ?? message.From.FirstName;

            string command = null;
            if (!string.IsNullOrEmpty(message.Text)) {
                command = message.Text.Trim();
            } else if (!string.IsNullOrEmpty(message.Caption)) {
                command = message.Caption.Trim();
            }

            if (string.IsNullOrEmpty(command))
                return;

            var view = new AccountView(_botClient, _sendMessage)
                .WithChatId(chatId)
                .WithReplyTo(message.MessageId);

            try {
                // 创建账本
                if (command.StartsWith("/创建账本 ") || command.StartsWith("创建账本 ")) {
                    await HandleCreateAccountBook(command, chatId, userId, view);
                }
                // 账本列表
                else if (command.Equals("/账本列表", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("账本列表", StringComparison.OrdinalIgnoreCase)) {
                    await HandleListAccountBooks(chatId, view);
                }
                // 激活账本
                else if (command.StartsWith("/激活账本 ") || command.StartsWith("激活账本 ")) {
                    await HandleActivateAccountBook(command, chatId, view);
                }
                // 查看记录
                else if (command.Equals("/记录", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("记录", StringComparison.OrdinalIgnoreCase)) {
                    await HandleListRecords(chatId, view);
                }
                // 统计信息
                else if (command.Equals("/统计", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("统计", StringComparison.OrdinalIgnoreCase)) {
                    await HandleStatistics(chatId, view);
                }
                // 统计图表
                else if (command.Equals("/统计图表", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("统计图表", StringComparison.OrdinalIgnoreCase)) {
                    await HandleStatisticsChart(chatId, view);
                }
                // 删除记录
                else if (command.StartsWith("/delrecord_")) {
                    await HandleDeleteRecord(command, userId, view);
                }
                // 删除账本
                else if (command.StartsWith("/删除账本 ") || command.StartsWith("删除账本 ")) {
                    await HandleDeleteAccountBook(command, chatId, userId, view);
                }
                // 帮助
                else if (command.Equals("/记账帮助", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("记账帮助", StringComparison.OrdinalIgnoreCase)) {
                    await view.WithHelp().Render();
                }
                // 快捷记账
                else {
                    var parseResult = _accountService.ParseQuickRecord(command);
                    if (parseResult.success) {
                        await HandleQuickRecord(chatId, userId, username, parseResult.amount, parseResult.tag, parseResult.description, view);
                    }
                }
            } catch (Exception ex) {
                await view.WithText($"处理命令时发生错误: {ex.Message}").Render();
            }
        }

        public async Task ExecuteAsync(Update update) {
            // 检查账本功能是否启用
            if (!Env.EnableAccounting) {
                return; // 功能未启用，直接返回
            }

            if (update.CallbackQuery == null) return;

            var callbackQuery = update.CallbackQuery;
            var chatId = callbackQuery.Message.Chat.Id;
            var data = callbackQuery.Data;

            var view = new AccountView(_botClient, _sendMessage)
                .WithChatId(chatId);

            try {
                if (data.StartsWith("account_activate_")) {
                    var accountBookId = long.Parse(data.Replace("account_activate_", ""));
                    var result = await _accountService.SetActiveAccountBookAsync(chatId, accountBookId);
                    await view.WithText(result.message).Render();
                } else if (data.StartsWith("account_records_page_")) {
                    var page = int.Parse(data.Replace("account_records_page_", ""));
                    await HandleListRecords(chatId, view, page);
                }

                // 回应callback query
                await _botClient.AnswerCallbackQuery(callbackQuery.Id);
            } catch (Exception ex) {
                await view.WithText($"处理回调时发生错误: {ex.Message}").Render();
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "操作失败");
            }
        }

        private async Task HandleCreateAccountBook(string command, long chatId, long userId, AccountView view) {
            var parts = command.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                await view.WithText("请提供账本名称。用法: /创建账本 名称 [描述]").Render();
                return;
            }

            var name = parts[1];
            var description = parts.Length > 2 ? parts[2] : null;

            var result = await _accountService.CreateAccountBookAsync(chatId, userId, name, description);
            await view.WithText(result.message).Render();
        }

        private async Task HandleListAccountBooks(long chatId, AccountView view) {
            var accountBooks = await _accountService.GetAccountBooksAsync(chatId);
            var activeBook = await _accountService.GetActiveAccountBookAsync(chatId);

            await view.WithAccountBooks(accountBooks, activeBook?.Id).Render();
        }

        private async Task HandleActivateAccountBook(string command, long chatId, AccountView view) {
            var parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                await view.WithText("请提供账本名称。用法: /激活账本 名称").Render();
                return;
            }

            var name = parts[1];
            var accountBooks = await _accountService.GetAccountBooksAsync(chatId);
            var targetBook = accountBooks.FirstOrDefault(ab => ab.Name == name);

            if (targetBook == null) {
                await view.WithText($"未找到名为 '{name}' 的账本").Render();
                return;
            }

            var result = await _accountService.SetActiveAccountBookAsync(chatId, targetBook.Id);
            await view.WithText(result.message).Render();
        }

        private async Task HandleListRecords(long chatId, AccountView view, int page = 1) {
            var activeBook = await _accountService.GetActiveAccountBookAsync(chatId);
            if (activeBook == null) {
                await view.WithText("当前群组没有激活的账本，请先创建并激活一个账本").Render();
                return;
            }

            const int pageSize = 10;
            var records = await _accountService.GetRecordsAsync(activeBook.Id, page, pageSize);

            // 计算总页数 (这里简化处理，实际应该获取总记录数)
            var totalPages = Math.Max(1, ( records.Count + pageSize - 1 ) / pageSize);

            await view.WithRecords(records, page, totalPages).Render();
        }

        private async Task HandleStatistics(long chatId, AccountView view) {
            var activeBook = await _accountService.GetActiveAccountBookAsync(chatId);
            if (activeBook == null) {
                await view.WithText("当前群组没有激活的账本，请先创建并激活一个账本").Render();
                return;
            }

            var stats = await _accountService.GetStatisticsAsync(activeBook.Id);
            await view.WithStatistics(stats).Render();
        }

        private async Task HandleStatisticsChart(long chatId, AccountView view) {
            var activeBook = await _accountService.GetActiveAccountBookAsync(chatId);
            if (activeBook == null) {
                await view.WithText("当前群组没有激活的账本，请先创建并激活一个账本").Render();
                return;
            }

            var chartData = await _accountService.GenerateStatisticsChartAsync(activeBook.Id);
            if (chartData == null || chartData.Length == 0) {
                await view.WithText("暂无数据生成图表").Render();
                return;
            }

            var stats = await _accountService.GetStatisticsAsync(activeBook.Id);
            var caption = $"📊 支出分类统计图表\n💸 总支出: {( decimal ) stats["totalExpense"]:F2} 元";

            await view.WithImage(chartData).WithText(caption).Render();
        }

        private async Task HandleDeleteRecord(string command, long userId, AccountView view) {
            var recordIdStr = command.Replace("/delrecord_", "");
            if (!long.TryParse(recordIdStr, out var recordId)) {
                await view.WithText("无效的记录ID").Render();
                return;
            }

            var result = await _accountService.DeleteRecordAsync(recordId, userId);
            await view.WithText(result.message).Render();
        }

        private async Task HandleDeleteAccountBook(string command, long chatId, long userId, AccountView view) {
            var parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                await view.WithText("请提供账本名称。用法: /删除账本 名称").Render();
                return;
            }

            var name = parts[1];
            var accountBooks = await _accountService.GetAccountBooksAsync(chatId);
            var targetBook = accountBooks.FirstOrDefault(ab => ab.Name == name);

            if (targetBook == null) {
                await view.WithText($"未找到名为 '{name}' 的账本").Render();
                return;
            }

            var result = await _accountService.DeleteAccountBookAsync(targetBook.Id, userId);
            await view.WithText(result.message).Render();
        }

        private async Task HandleQuickRecord(long chatId, long userId, string username, decimal amount, string tag, string description, AccountView view) {
            var result = await _accountService.AddRecordAsync(chatId, userId, username, amount, tag, description);
            await view.WithText(result.message).Render();
        }
    }
}
