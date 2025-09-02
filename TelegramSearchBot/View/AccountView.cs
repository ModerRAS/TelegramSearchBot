using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.View {
    public class AccountView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _textContent;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private byte[] _imageData;

        public AccountView(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public class ViewButton {
            public string Text { get; set; }
            public string CallbackData { get; set; }

            public ViewButton(string text, string callbackData) {
                Text = text;
                CallbackData = callbackData;
            }
        }

        // Fluent API methods
        public AccountView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public AccountView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public AccountView WithText(string text) {
            _textContent = MessageFormatHelper.ConvertMarkdownToTelegramHtml(text);
            return this;
        }

        public AccountView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public AccountView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public AccountView WithImage(byte[] imageData) {
            _imageData = imageData;
            return this;
        }

        public AccountView WithAccountBooks(List<AccountBook> accountBooks, long? activeBookId = null) {
            var sb = new StringBuilder();
            sb.AppendLine("📚 <b>账本列表</b>");
            sb.AppendLine();

            if (!accountBooks.Any()) {
                sb.AppendLine("暂无账本，请先创建一个账本。");
            } else {
                foreach (var book in accountBooks) {
                    var icon = book.Id == activeBookId ? "🟢" : "⚪";
                    var status = book.Id == activeBookId ? " (当前激活)" : "";
                    sb.AppendLine($"{icon} <b>{book.Name}</b>{status}");
                    if (!string.IsNullOrWhiteSpace(book.Description)) {
                        sb.AppendLine($"   📝 {book.Description}");
                    }
                    sb.AppendLine($"   👤 创建者: {book.CreatedBy}");
                    sb.AppendLine($"   📅 创建时间: {book.CreatedAt:yyyy-MM-dd HH:mm}");
                    sb.AppendLine();

                    // 添加账本操作按钮
                    if (book.Id != activeBookId) {
                        AddButton($"激活 {book.Name}", $"account_activate_{book.Id}");
                    }
                }
            }

            _textContent = sb.ToString();
            return this;
        }

        public AccountView WithRecords(List<AccountRecord> records, int page = 1, int totalPages = 1) {
            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>记账记录</b>");
            sb.AppendLine();

            if (!records.Any()) {
                sb.AppendLine("暂无记录。");
            } else {
                foreach (var record in records) {
                    var icon = record.Amount >= 0 ? "💰" : "💸";
                    var type = record.Amount >= 0 ? "收入" : "支出";
                    var amount = Math.Abs(record.Amount);

                    sb.AppendLine($"{icon} <b>{type}</b>: {amount:F2} 元");
                    sb.AppendLine($"🏷️ 标签: <code>{record.Tag}</code>");
                    if (!string.IsNullOrWhiteSpace(record.Description)) {
                        sb.AppendLine($"📝 说明: {record.Description}");
                    }
                    sb.AppendLine($"👤 记录者: {record.CreatedByUsername ?? record.CreatedBy.ToString()}");
                    sb.AppendLine($"📅 时间: {record.CreatedAt:yyyy-MM-dd HH:mm}");
                    sb.AppendLine($"🗑️ /delrecord_{record.Id}");
                    sb.AppendLine();
                }

                // 分页按钮
                if (totalPages > 1) {
                    if (page > 1) {
                        AddButton("⬅️ 上一页", $"account_records_page_{page - 1}");
                    }
                    AddButton($"📄 {page}/{totalPages}", "noop");
                    if (page < totalPages) {
                        AddButton("➡️ 下一页", $"account_records_page_{page + 1}");
                    }
                }
            }

            _textContent = sb.ToString();
            return this;
        }

        public AccountView WithStatistics(Dictionary<string, object> stats) {
            var sb = new StringBuilder();
            sb.AppendLine("📈 <b>统计信息</b>");
            sb.AppendLine();

            var totalIncome = ( decimal ) stats["totalIncome"];
            var totalExpense = ( decimal ) stats["totalExpense"];
            var balance = ( decimal ) stats["balance"];
            var recordCount = ( int ) stats["recordCount"];

            sb.AppendLine($"💰 <b>总收入</b>: {totalIncome:F2} 元");
            sb.AppendLine($"💸 <b>总支出</b>: {totalExpense:F2} 元");
            sb.AppendLine($"💳 <b>净收支</b>: {balance:F2} 元");
            sb.AppendLine($"📝 <b>记录数</b>: {recordCount} 条");
            sb.AppendLine();

            var expenseByTag = ( Dictionary<string, decimal> ) stats["expenseByTag"];
            if (expenseByTag.Any()) {
                sb.AppendLine("📊 <b>支出分类</b>:");
                foreach (var item in expenseByTag.OrderByDescending(x => x.Value).Take(10)) {
                    var percentage = totalExpense > 0 ? ( item.Value / totalExpense * 100 ) : 0;
                    sb.AppendLine($"   🏷️ {item.Key}: {item.Value:F2} 元 ({percentage:F1}%)");
                }
                sb.AppendLine();
            }

            var incomeByTag = ( Dictionary<string, decimal> ) stats["incomeByTag"];
            if (incomeByTag.Any()) {
                sb.AppendLine("💎 <b>收入分类</b>:");
                foreach (var item in incomeByTag.OrderByDescending(x => x.Value).Take(5)) {
                    var percentage = totalIncome > 0 ? ( item.Value / totalIncome * 100 ) : 0;
                    sb.AppendLine($"   🏷️ {item.Key}: {item.Value:F2} 元 ({percentage:F1}%)");
                }
            }

            _textContent = sb.ToString();
            return this;
        }

        public AccountView WithHelp() {
            var sb = new StringBuilder();
            sb.AppendLine("💡 <b>记账功能帮助</b>");
            sb.AppendLine();
            sb.AppendLine("📚 <b>账本管理</b>");
            sb.AppendLine("• <code>/创建账本 账本名称 [描述]</code> - 创建新账本");
            sb.AppendLine("• <code>/账本列表</code> - 查看所有账本");
            sb.AppendLine("• <code>/激活账本 账本名称</code> - 激活指定账本");
            sb.AppendLine();
            sb.AppendLine("📝 <b>记账操作</b>");
            sb.AppendLine("• <code>金额 标签 [说明]</code> - 快速记账");
            sb.AppendLine("  例如: <code>-50 餐费 午餐</code>");
            sb.AppendLine("  例如: <code>+1000 工资 月薪</code>");
            sb.AppendLine("  例如: <code>30.5 零食</code>");
            sb.AppendLine();
            sb.AppendLine("📊 <b>查看数据</b>");
            sb.AppendLine("• <code>/记录</code> - 查看记录列表");
            sb.AppendLine("• <code>/统计</code> - 查看统计信息");
            sb.AppendLine("• <code>/统计图表</code> - 生成统计图表");
            sb.AppendLine();
            sb.AppendLine("🗑️ <b>删除操作</b>");
            sb.AppendLine("• <code>/delrecord_记录ID</code> - 删除指定记录");
            sb.AppendLine("• <code>/删除账本 账本名称</code> - 删除账本");
            sb.AppendLine();
            sb.AppendLine("💰 <b>金额说明</b>");
            sb.AppendLine("• 正数或不带符号表示收入");
            sb.AppendLine("• 负数表示支出");
            sb.AppendLine("• 支持小数，最多两位小数");

            _textContent = sb.ToString();
            return this;
        }

        public async Task Render() {
            try {
                var replyParameters = new ReplyParameters {
                    MessageId = _replyToMessageId
                };

                var inlineButtons = _buttons?.Select(b =>
                    InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

                var replyMarkup = inlineButtons != null && inlineButtons.Any() ?
                    new InlineKeyboardMarkup(inlineButtons) : null;

                if (_imageData != null && _imageData.Length > 0) {
                    // 发送图片消息
                    using var stream = new MemoryStream(_imageData);
                    var inputFile = new InputFileStream(stream, "chart.png");

                    await _sendMessage.AddTaskWithResult(async () => await _botClient.SendPhoto(
                        chatId: _chatId,
                        photo: inputFile,
                        caption: _textContent,
                        parseMode: ParseMode.Html,
                        replyParameters: replyParameters,
                        disableNotification: _disableNotification,
                        replyMarkup: replyMarkup
                    ), _chatId);
                } else {
                    // 发送文本消息
                    await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                        chatId: _chatId,
                        text: _textContent,
                        parseMode: ParseMode.Html,
                        replyParameters: replyParameters,
                        disableNotification: _disableNotification,
                        replyMarkup: replyMarkup
                    ), _chatId);
                }
            } catch (Exception ex) {
                // 日志记录异常，但不重新抛出
                // 可以考虑发送错误消息给用户
                await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                    chatId: _chatId,
                    text: "发送消息时发生错误，请稍后重试。",
                    parseMode: ParseMode.Html
                ), _chatId);
            }
        }
    }
}
