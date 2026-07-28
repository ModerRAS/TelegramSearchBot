using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

#nullable enable

namespace TelegramSearchBot.Interface.AI.LLM {
    public sealed class GroupAgentChatSettings {
        public const int DefaultBatchWindowSeconds = 5;

        public bool IsEnabled { get; set; }
        public GroupAgentChatMode Mode { get; set; } = GroupAgentChatMode.GuidedBatch;
        public int BatchWindowSeconds { get; set; } = DefaultBatchWindowSeconds;
        public string? ModelName { get; set; }
    }

    public interface IGroupLlmSettingsService {
        Task<string?> GetModelAsync(long chatId, CancellationToken cancellationToken = default);
        Task<(string Previous, string Current)> SetModelAsync(long chatId, string modelName, CancellationToken cancellationToken = default);
        Task<GroupAgentChatSettings> GetAgentChatSettingsAsync(long chatId, CancellationToken cancellationToken = default);
        Task<GroupAgentChatSettings> SetAgentChatModeAsync(
            long chatId,
            bool isEnabled,
            GroupAgentChatMode mode,
            int? batchWindowSeconds = null,
            CancellationToken cancellationToken = default);
    }
}
