using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Bilibili;

public interface ITelegramFileCacheService {
    /// <summary>
    /// Gets a cached Telegram file_id for a given key (e.g., original media URL or a unique filename).
    /// </summary>
    /// <param name="cacheKey">The unique key associated with the media file.</param>
    /// <returns>The cached file_id, or null if not found.</returns>
    Task<string> GetCachedFileIdAsync(string cacheKey);

    /// <summary>
    /// Caches a Telegram file_id against a given key.
    /// </summary>
    /// <param name="cacheKey">The unique key to associate with the media file.</param>
    /// <param name="fileId">The Telegram file_id to cache.</param>
    /// <param name="expiry">Optional: TimeSpan after which the cache entry should expire. Defaults to no expiry if null.</param>
    Task CacheFileIdAsync(string cacheKey, string fileId, System.TimeSpan? expiry = null);

    /// <summary>
    /// Deletes a cached Telegram file_id for a given key.
    /// </summary>
    /// <param name="cacheKey">The unique key associated with the media file to delete.</param>
    /// <returns>True if the key was removed, false otherwise.</returns>
    Task<bool> DeleteCachedFileIdAsync(string cacheKey);
}
