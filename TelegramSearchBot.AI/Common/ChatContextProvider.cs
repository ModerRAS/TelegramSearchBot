using System.Threading;
using System; // For InvalidOperationException

namespace TelegramSearchBot.Service.Common
{
    [Obsolete("ChatContextProvider is deprecated. Use ToolContext instead for passing ChatId to tools.")]
    public static class ChatContextProvider
    {
        private static readonly AsyncLocal<long?> _currentChatId = new AsyncLocal<long?>();

        public static void SetCurrentChatId(long chatId)
        {
            _currentChatId.Value = chatId;
        }

        public static long GetCurrentChatId(bool throwIfNotFound = true)
        {
            var chatId = _currentChatId.Value;
            if (chatId == null && throwIfNotFound)
            {
                throw new InvalidOperationException("ChatId not found in the current context. Ensure SetCurrentChatId was called.");
            }
            // 简化实现：使用空值合并运算符避免潜在的空引用问题
            // 原本实现：使用chatId.GetValueOrDefault()
            // 简化实现：使用更安全的空值处理方式
            return chatId ?? 0;
        }

        public static void Clear()
        {
            _currentChatId.Value = null;
        }
    }
}