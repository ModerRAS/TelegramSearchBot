using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Service for managing and retrieving group-specific settings.
    /// </summary>
    public interface IGroupSettingsService
    {
        /// <summary>
        /// Gets the preferred LLM model name for a specific chat.
        /// </summary>
        /// <param name="chatId">The ID of the chat.</param>
        /// <returns>The preferred LLM model name, or null if not set.</returns>
        Task<string?> GetLlmModelForChatAsync(long chatId);

        /// <summary>
        /// Sets the preferred LLM model name for a specific chat.
        /// </summary>
        /// <param name="chatId">The ID of the chat.</param>
        /// <param name="modelName">The friendly name of the LLM model.</param>
        Task SetLlmModelForChatAsync(long chatId, string modelName);

        /// <summary>
        /// Checks if a user is an administrator or creator of a specific chat.
        /// </summary>
        /// <param name="chatId">The ID of the chat.</param>
        /// <param name="userId">The ID of the user.</param>
        /// <returns>True if the user is an admin or creator, false otherwise.</returns>
        Task<bool> IsUserChatAdminAsync(long chatId, long userId);
    }
}
