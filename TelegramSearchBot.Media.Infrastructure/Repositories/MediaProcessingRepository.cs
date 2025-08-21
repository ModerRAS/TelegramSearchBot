using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Media.Domain.ValueObjects;
using TelegramSearchBot.Media.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Media.Infrastructure.Repositories
{
    /// <summary>
    /// 媒体处理仓储实现（简化版本，使用文件系统存储）
    /// </summary>
    public class MediaProcessingRepository : IMediaProcessingRepository
    {
        private readonly string _storagePath;
        private readonly string _cachePath;
        private readonly ILogger<MediaProcessingRepository> _logger;
        private readonly Dictionary<string, MediaProcessingAggregate> _inMemoryStorage = new Dictionary<string, MediaProcessingAggregate>();

        public MediaProcessingRepository(string storagePath = "./media_storage", string cachePath = "./media_cache", 
            ILogger<MediaProcessingRepository> logger = null)
        {
            _storagePath = storagePath;
            _cachePath = cachePath;
            _logger = logger;

            // 确保目录存在
            Directory.CreateDirectory(storagePath);
            Directory.CreateDirectory(cachePath);
        }

        public async Task SaveAsync(MediaProcessingAggregate aggregate)
        {
            try
            {
                var key = aggregate.Id.Value.ToString();
                _inMemoryStorage[key] = aggregate;

                // 保存到文件系统（简化实现）
                var filePath = Path.Combine(_storagePath, $"{key}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(aggregate);
                await File.WriteAllTextAsync(filePath, json);

                _logger?.LogInformation("Saved media processing aggregate {MediaProcessingId}", aggregate.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving media processing aggregate {MediaProcessingId}", aggregate.Id);
                throw;
            }
        }

        public async Task<MediaProcessingAggregate> GetByIdAsync(MediaProcessingId id)
        {
            try
            {
                var key = id.Value.ToString();
                
                // 先从内存中查找
                if (_inMemoryStorage.TryGetValue(key, out var aggregate))
                {
                    return aggregate;
                }

                // 从文件系统加载
                var filePath = Path.Combine(_storagePath, $"{key}.json");
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                aggregate = System.Text.Json.JsonSerializer.Deserialize<MediaProcessingAggregate>(json);
                
                // 添加到内存缓存
                _inMemoryStorage[key] = aggregate;

                return aggregate;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading media processing aggregate {MediaProcessingId}", id);
                throw;
            }
        }

        public async Task<MediaProcessingAggregate> GetBySourceUrlAsync(string sourceUrl)
        {
            try
            {
                // 简化实现：遍历所有聚合查找匹配的源URL
                foreach (var aggregate in _inMemoryStorage.Values)
                {
                    if (aggregate.MediaInfo.SourceUrl.Equals(sourceUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return aggregate;
                    }
                }

                // 从文件系统查找
                var files = Directory.GetFiles(_storagePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var aggregate = System.Text.Json.JsonSerializer.Deserialize<MediaProcessingAggregate>(json);
                        
                        if (aggregate.MediaInfo.SourceUrl.Equals(sourceUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            return aggregate;
                        }
                    }
                    catch
                    {
                        // 忽略单个文件的错误，继续处理其他文件
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error searching media processing aggregate by source URL {SourceUrl}", sourceUrl);
                throw;
            }
        }

        public async Task<MediaProcessingAggregate[]> GetPendingAsync(int maxCount = 10)
        {
            return await GetByStatusAsync(MediaProcessingStatus.Pending, maxCount);
        }

        public async Task<MediaProcessingAggregate[]> GetProcessingAsync(int maxCount = 10)
        {
            return await GetByStatusAsync(MediaProcessingStatus.Processing, maxCount);
        }

        public async Task<MediaProcessingAggregate[]> GetCompletedAsync(int maxCount = 10)
        {
            return await GetByStatusAsync(MediaProcessingStatus.Completed, maxCount);
        }

        public async Task<MediaProcessingAggregate[]> GetFailedAsync(int maxCount = 10)
        {
            return await GetByStatusAsync(MediaProcessingStatus.Failed, maxCount);
        }

        private async Task<MediaProcessingAggregate[]> GetByStatusAsync(MediaProcessingStatus status, int maxCount)
        {
            try
            {
                var result = new List<MediaProcessingAggregate>();

                // 从内存中查找
                foreach (var aggregate in _inMemoryStorage.Values)
                {
                    if (aggregate.HasStatus(status))
                    {
                        result.Add(aggregate);
                        if (result.Count >= maxCount)
                            break;
                    }
                }

                if (result.Count >= maxCount)
                {
                    return result.ToArray();
                }

                // 从文件系统查找
                var files = Directory.GetFiles(_storagePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var aggregate = System.Text.Json.JsonSerializer.Deserialize<MediaProcessingAggregate>(json);
                        
                        if (aggregate.HasStatus(status) && !result.Contains(aggregate))
                        {
                            result.Add(aggregate);
                            if (result.Count >= maxCount)
                                break;
                        }
                    }
                    catch
                    {
                        // 忽略单个文件的错误，继续处理其他文件
                    }
                }

                return result.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting media processing aggregates by status {Status}", status);
                throw;
            }
        }

        public async Task<bool> IsFileCachedAsync(string cacheKey)
        {
            try
            {
                var cacheFilePath = Path.Combine(_cachePath, cacheKey);
                return File.Exists(cacheFilePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking cache file {CacheKey}", cacheKey);
                return false;
            }
        }

        public async Task CacheFileAsync(string cacheKey, string filePath)
        {
            try
            {
                var cacheFilePath = Path.Combine(_cachePath, cacheKey);
                
                // 确保缓存目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath));
                
                // 复制文件到缓存
                await File.CopyAsync(filePath, cacheFilePath, true);
                
                _logger?.LogInformation("Cached file {CacheKey} to {CacheFilePath}", cacheKey, cacheFilePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error caching file {CacheKey} from {FilePath}", cacheKey, filePath);
                throw;
            }
        }

        public async Task<string> GetCachedFileAsync(string cacheKey)
        {
            try
            {
                var cacheFilePath = Path.Combine(_cachePath, cacheKey);
                
                if (File.Exists(cacheFilePath))
                {
                    return cacheFilePath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting cached file {CacheKey}", cacheKey);
                return null;
            }
        }

        public async Task CleanupExpiredCacheAsync(TimeSpan expiration)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow - expiration;
                var cacheFiles = Directory.GetFiles(_cachePath, "*.*", SearchOption.AllDirectories);

                foreach (var file in cacheFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTimeUtc < cutoffTime)
                        {
                            File.Delete(file);
                            _logger?.LogInformation("Deleted expired cache file {FilePath}", file);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error deleting cache file {FilePath}", file);
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning up expired cache");
                throw;
            }
        }

        public async Task DeleteAsync(MediaProcessingId id)
        {
            try
            {
                var key = id.Value.ToString();
                
                // 从内存中删除
                _inMemoryStorage.Remove(key);
                
                // 从文件系统删除
                var filePath = Path.Combine(_storagePath, $"{key}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                _logger?.LogInformation("Deleted media processing aggregate {MediaProcessingId}", id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting media processing aggregate {MediaProcessingId}", id);
                throw;
            }
        }

        public async Task<MediaProcessingStats> GetStatsAsync()
        {
            try
            {
                var stats = new MediaProcessingStats();

                // 统计内存中的聚合
                foreach (var aggregate in _inMemoryStorage.Values)
                {
                    UpdateStats(stats, aggregate);
                }

                // 统计文件系统中的聚合
                var files = Directory.GetFiles(_storagePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var aggregate = System.Text.Json.JsonSerializer.Deserialize<MediaProcessingAggregate>(json);
                        UpdateStats(stats, aggregate);
                    }
                    catch
                    {
                        // 忽略单个文件的错误，继续处理其他文件
                    }
                }

                // 计算缓存大小
                var cacheFiles = Directory.GetFiles(_cachePath, "*.*", SearchOption.AllDirectories);
                stats.CacheSize = cacheFiles.Sum(f => new FileInfo(f).Length);

                return stats;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting media processing stats");
                throw;
            }
        }

        private void UpdateStats(MediaProcessingStats stats, MediaProcessingAggregate aggregate)
        {
            stats.TotalCount++;

            if (aggregate.HasStatus(MediaProcessingStatus.Pending))
                stats.PendingCount++;
            else if (aggregate.HasStatus(MediaProcessingStatus.Processing))
                stats.ProcessingCount++;
            else if (aggregate.HasStatus(MediaProcessingStatus.Completed))
                stats.CompletedCount++;
            else if (aggregate.HasStatus(MediaProcessingStatus.Failed))
                stats.FailedCount++;
            else if (aggregate.HasStatus(MediaProcessingStatus.Cancelled))
                stats.CancelledCount++;

            if (aggregate.Result != null && aggregate.Result.Success)
            {
                stats.TotalFileSize += aggregate.Result.FileSize;
            }
        }
    }
}