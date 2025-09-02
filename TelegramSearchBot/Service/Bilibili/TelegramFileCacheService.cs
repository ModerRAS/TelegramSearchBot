using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Bilibili;

[Injectable(ServiceLifetime.Transient)]
public class TelegramFileCacheService : ITelegramFileCacheService {
    private readonly DataDbContext _dbContext;
    private readonly ILogger<TelegramFileCacheService> _logger;

    public TelegramFileCacheService(DataDbContext dbContext, ILogger<TelegramFileCacheService> logger) {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string> GetCachedFileIdAsync(string cacheKey) {
        if (string.IsNullOrWhiteSpace(cacheKey)) return null;

        try {
            var cacheEntry = await _dbContext.TelegramFileCacheEntries
                .FirstOrDefaultAsync(e => e.CacheKey == cacheKey);

            if (cacheEntry != null) {
                if (cacheEntry.ExpiryDate.HasValue && cacheEntry.ExpiryDate.Value < DateTime.UtcNow) {
                    _logger.LogInformation("Cache entry for key: {CacheKey} has expired. Removing.", cacheKey);
                    _dbContext.TelegramFileCacheEntries.Remove(cacheEntry);
                    await _dbContext.SaveChangesAsync();
                    return null;
                }
                _logger.LogDebug("Cache hit for file_id with key: {CacheKey}", cacheKey);
                return cacheEntry.FileId;
            }
            _logger.LogDebug("Cache miss for file_id with key: {CacheKey}", cacheKey);
            return null;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting cached file_id from database for key: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task CacheFileIdAsync(string cacheKey, string fileId, TimeSpan? expiry = null) {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(fileId)) {
            _logger.LogWarning("Cache key or fileId is empty, skipping cache.");
            return;
        }

        try {
            var existingEntry = await _dbContext.TelegramFileCacheEntries
                .FirstOrDefaultAsync(e => e.CacheKey == cacheKey);

            DateTime? expiryDate = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null;

            if (existingEntry != null) {
                existingEntry.FileId = fileId;
                existingEntry.ExpiryDate = expiryDate;
                _dbContext.TelegramFileCacheEntries.Update(existingEntry);
                _logger.LogInformation("Updated cached file_id for key: {CacheKey} with expiry: {ExpiryDate}", cacheKey, expiryDate?.ToString() ?? "None");
            } else {
                var newEntry = new TelegramFileCacheEntry {
                    CacheKey = cacheKey,
                    FileId = fileId,
                    ExpiryDate = expiryDate
                };
                await _dbContext.TelegramFileCacheEntries.AddAsync(newEntry);
                _logger.LogInformation("Cached file_id for key: {CacheKey} with expiry: {ExpiryDate}", cacheKey, expiryDate?.ToString() ?? "None");
            }
            await _dbContext.SaveChangesAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error caching file_id to database for key: {CacheKey}", cacheKey);
        }
    }

    public async Task<bool> DeleteCachedFileIdAsync(string cacheKey) {
        if (string.IsNullOrWhiteSpace(cacheKey)) return false;

        try {
            var cacheEntry = await _dbContext.TelegramFileCacheEntries
                .FirstOrDefaultAsync(e => e.CacheKey == cacheKey);

            if (cacheEntry != null) {
                _dbContext.TelegramFileCacheEntries.Remove(cacheEntry);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Deleted cached file_id for key: {CacheKey}", cacheKey);
                return true;
            }
            return false;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting cached file_id from database for key: {CacheKey}", cacheKey);
            return false;
        }
    }
}
