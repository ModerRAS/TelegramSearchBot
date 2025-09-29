using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace TelegramSearchBot.Search.Tool {
    public class ExtFieldQueryOptimizer {
        private readonly ConcurrentDictionary<long, (string[] Fields, DateTime CachedAt)> _fieldCache = new();
        private readonly Action<string>? _logAction;
        private const int MaxCacheSize = 100;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);

        public ExtFieldQueryOptimizer(Action<string>? logAction = null) {
            _logAction = logAction;
        }

        public BooleanQuery BuildOptimizedExtQuery(List<string> keywords, IndexReader reader, long groupId) {
            var query = new BooleanQuery();
            var extFields = GetExtFields(reader, groupId);

            if (extFields.Length == 0) {
                return query;
            }

            foreach (var keyword in keywords) {
                if (string.IsNullOrWhiteSpace(keyword)) {
                    continue;
                }

                foreach (var field in extFields) {
                    query.Add(new TermQuery(new Term(field, keyword)), Occur.SHOULD);
                }
            }

            return query;
        }

        public BooleanQuery BuildOptimizedExtPhraseQuery(List<string> terms, IndexReader reader, long groupId) {
            var combinedQuery = new BooleanQuery();
            var extFields = GetExtFields(reader, groupId);

            if (extFields.Length == 0) {
                return combinedQuery;
            }

            foreach (var field in extFields) {
                combinedQuery.Add(BuildPhraseQueryForField(field, terms), Occur.SHOULD);
            }

            return combinedQuery;
        }

        public BooleanQuery BuildOptimizedExtExcludeQuery(List<string> excludeKeywords, IndexReader reader, long groupId) {
            var excludeQuery = new BooleanQuery();
            var extFields = GetExtFields(reader, groupId);

            if (extFields.Length == 0) {
                return excludeQuery;
            }

            foreach (var keyword in excludeKeywords) {
                if (string.IsNullOrWhiteSpace(keyword)) {
                    continue;
                }

                var keywordExcludeQuery = new BooleanQuery();
                foreach (var field in extFields) {
                    keywordExcludeQuery.Add(new TermQuery(new Term(field, keyword)), Occur.SHOULD);
                }

                if (keywordExcludeQuery.Clauses.Count > 0) {
                    excludeQuery.Add(keywordExcludeQuery, Occur.SHOULD);
                }
            }

            return excludeQuery;
        }

        public void ClearCache(long groupId = -1) {
            if (groupId == -1) {
                _fieldCache.Clear();
            } else {
                _fieldCache.TryRemove(groupId, out _);
            }
        }

        private PhraseQuery BuildPhraseQueryForField(string fieldName, List<string> terms) {
            var phraseQuery = new PhraseQuery();
            for (int i = 0; i < terms.Count; i++) {
                phraseQuery.Add(new Term(fieldName, terms[i]), i);
            }
            return phraseQuery;
        }

        private string[] GetExtFields(IndexReader reader, long groupId) {
            CleanupExpiredCache();

            if (_fieldCache.TryGetValue(groupId, out var cachedData)) {
                if (DateTime.Now - cachedData.CachedAt < CacheExpiry) {
                    return cachedData.Fields;
                }

                _fieldCache.TryRemove(groupId, out _);
            }

            try {
                var fields = MultiFields.GetIndexedFields(reader);
                var extFields = fields.Where(f => f.StartsWith("Ext_", StringComparison.Ordinal)).ToArray();

                if (_fieldCache.Count >= MaxCacheSize) {
                    var oldestKey = _fieldCache.OrderBy(kv => kv.Value.CachedAt).First().Key;
                    _fieldCache.TryRemove(oldestKey, out _);
                }

                _fieldCache.TryAdd(groupId, (extFields, DateTime.Now));
                _logAction?.Invoke($"GroupId {groupId}: 发现 {extFields.Length} 个Ext字段");
                return extFields;
            } catch (System.Exception ex) {
                _logAction?.Invoke($"获取Ext字段失败: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private void CleanupExpiredCache() {
            var now = DateTime.Now;
            var expiredKeys = _fieldCache
                .Where(kv => now - kv.Value.CachedAt >= CacheExpiry)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredKeys) {
                _fieldCache.TryRemove(key, out _);
            }
        }
    }
}
