using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Core.Attributes;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Service.Search {
    /// <summary>
    /// 搜索选项存储服务，负责管理搜索选项的缓存和分页逻辑
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SearchOptionStorageService : IService {
        public string ServiceName => "SearchOptionStorageService";
        private readonly DataDbContext _dataDbContext;
        private readonly ILogger<SearchOptionStorageService> _logger;

        public SearchOptionStorageService(
            DataDbContext dataDbContext,
            ILogger<SearchOptionStorageService> logger) {
            // 初始化搜索选项存储服务
            _dataDbContext = dataDbContext ?? throw new ArgumentNullException(nameof(dataDbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        /// <summary>
        /// 根据UUID获取搜索选项并在查询后删除数据库记录
        /// </summary>
        /// <param name="UUID">唯一标识符，用于查找对应的搜索选项</param>
        /// <returns>找到的SearchOption对象</returns>
        /// <exception cref="ArgumentException">当UUID为空或null时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到对应UUID的缓存时抛出</exception>
        /// <exception cref="InvalidOperationException">当找到的缓存中SearchOption为null时抛出</exception>
        public async Task<SearchOption> GetAndRemoveSearchOptionAsync(string UUID) {
            if (string.IsNullOrEmpty(UUID)) {
                throw new ArgumentException("UUID cannot be null or empty");
            }

            try {
                // 从SearchPageCaches表中查找缓存
                var cache = await _dataDbContext.SearchPageCaches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.UUID == UUID)
                    .ConfigureAwait(false);

                if (cache == null) {
                    throw new KeyNotFoundException($"SearchPageCache with UUID {UUID} not found");
                }

                if (cache.SearchOption == null) {
                    throw new InvalidOperationException($"SearchOption for UUID {UUID} is null");
                }

                var result = cache.SearchOption;

                // 查询成功后删除记录
                _dataDbContext.SearchPageCaches.Remove(cache);
                await _dataDbContext.SaveChangesAsync().ConfigureAwait(false);

                return result;
            } catch (Exception ex) {
                // 记录日志
                _logger.LogError(ex, "Error retrieving search option for UUID {UUID}", UUID);
                throw;
            }
        }
        /// <summary>
        /// 保存搜索选项到数据库并返回生成的UUID
        /// </summary>
        /// <param name="searchOption">要保存的搜索选项对象</param>
        /// <returns>生成的UUID字符串，可用于后续检索</returns>
        /// <exception cref="ArgumentNullException">当searchOption为null时抛出</exception>
        public async Task<string> SetSearchOptionAsync(SearchOption searchOption) {
            if (searchOption == null) {
                throw new ArgumentNullException(nameof(searchOption));
            }

            try {
                var uuid = Guid.NewGuid().ToString();
                var cache = new SearchPageCache {
                    UUID = uuid,
                    SearchOption = searchOption
                };

                _dataDbContext.SearchPageCaches.Add(cache);
                await _dataDbContext.SaveChangesAsync().ConfigureAwait(false);

                return uuid;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error saving search option");
                throw;
            }
        }

        /// <summary>
        /// 获取下一个搜索选项（分页用）
        /// </summary>
        /// <param name="currentOption">当前搜索选项</param>
        /// <returns>更新了Skip后的新搜索选项，如果已到达末尾则返回null</returns>
        /// <exception cref="ArgumentNullException">当currentOption为null时抛出</exception>
        public SearchOption GetNextSearchOption(SearchOption currentOption) {
            if (currentOption == null) {
                throw new ArgumentNullException(nameof(currentOption));
            }

            if (currentOption.Skip >= currentOption.Count) {
                return null;
            }
            // 深拷贝当前搜索选项

            var nextOption = JsonConvert.DeserializeObject<SearchOption>(JsonConvert.SerializeObject(currentOption));
            nextOption.Skip += nextOption.Take;

            return nextOption.Skip < nextOption.Count ? nextOption : null;
        }

        /// <summary>
        /// 获取标记为立即删除的搜索选项
        /// </summary>
        /// <param name="currentOption">当前搜索选项</param>
        /// <returns>标记了ToDeleteNow属性的新搜索选项</returns>
        /// <exception cref="ArgumentNullException">当currentOption为null时抛出</exception>
        public SearchOption GetToDeleteNowSearchOption(SearchOption currentOption) {
            if (currentOption == null) {
                throw new ArgumentNullException(nameof(currentOption));
            }

            // 深拷贝当前搜索选项

            var nextOption = JsonConvert.DeserializeObject<SearchOption>(JsonConvert.SerializeObject(currentOption));
            nextOption.ToDeleteNow = true;

            return nextOption;
        }

        /// <summary>
        /// 清理指定时间间隔前的搜索页面缓存
        /// </summary>
        /// <param name="timeSpan">要清理的时间间隔</param>
        /// <returns>删除的记录数</returns>
        public async Task<int> CleanupOldSearchPageCachesAsync(TimeSpan timeSpan) {
            try {
                var cutoffTime = DateTime.UtcNow.Subtract(timeSpan);
                var oldCaches = _dataDbContext.SearchPageCaches
                    .Where(c => c.CreatedTime < cutoffTime);

                int deletedCount = await oldCaches.CountAsync().ConfigureAwait(false);

                if (deletedCount > 0) {
                    _dataDbContext.SearchPageCaches.RemoveRange(oldCaches);
                    await _dataDbContext.SaveChangesAsync().ConfigureAwait(false);
                    _logger.LogInformation("Deleted {Count} old search page caches older than {Date}",
                        deletedCount, cutoffTime);
                }

                return deletedCount;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error cleaning up old search page caches");
                throw;
            }
        }
    }
}
