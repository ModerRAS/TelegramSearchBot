using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TelegramSearchBot.Service.Bilibili;

public class TelegramFileCacheService : ITelegramFileCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TelegramFileCacheService> _logger;
    private const string FileIdCachePrefix = "telegram_file_id:";

    public TelegramFileCacheService(IConnectionMultiplexer redis, ILogger<TelegramFileCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private string GetRedisKey(string cacheKey)
    {
        return $"{FileIdCachePrefix}{cacheKey}";
    }

    public async Task<string> GetCachedFileIdAsync(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey)) return null;

        try
        {
            var db = _redis.GetDatabase();
            var redisValue = await db.StringGetAsync(GetRedisKey(cacheKey));
            if (redisValue.HasValue)
            {
                _logger.LogDebug("Cache hit for file_id with key: {CacheKey}", cacheKey);
                return redisValue.ToString();
            }
            _logger.LogDebug("Cache miss for file_id with key: {CacheKey}", cacheKey);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached file_id from Redis for key: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task CacheFileIdAsync(string cacheKey, string fileId, TimeSpan? expiry = null)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(fileId))
        {
            _logger.LogWarning("Cache key or fileId is empty, skipping cache.");
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(GetRedisKey(cacheKey), fileId, expiry);
            _logger.LogInformation("Cached file_id for key: {CacheKey} with expiry: {Expiry}", cacheKey, expiry?.ToString() ?? "None");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching file_id to Redis for key: {CacheKey}", cacheKey);
        }
    }

    public async Task<bool> DeleteCachedFileIdAsync(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey)) return false;

        try
        {
            var db = _redis.GetDatabase();
            var result = await db.KeyDeleteAsync(GetRedisKey(cacheKey));
            if(result)
            {
                _logger.LogInformation("Deleted cached file_id for key: {CacheKey}", cacheKey);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cached file_id from Redis for key: {CacheKey}", cacheKey);
            return false;
        }
    }
}
