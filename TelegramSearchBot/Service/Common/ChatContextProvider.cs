using System.Threading;
using System; // For InvalidOperationException

namespace TelegramSearchBot.Service.Common
{
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
            return chatId.GetValueOrDefault();
        }

        public static void Clear()
        {
            _currentChatId.Value = null;
        }
    }
}
