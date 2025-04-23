using TelegramSearchBot.AIApi.Model.ChatModel;

namespace TelegramSearchBot.AIApi.Interface
{
    public interface IChatService {
        Task<ChatResponse> GetChatAsync(ChatRequest request);
        IAsyncEnumerable<string> StreamChatAsync(ChatRequest request, CancellationToken cancellationToken);
    }

}
