using System;
using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Search;
using TelegramSearchBot.Search.Interface;
using TelegramSearchBot.Search.Tokenizer;

namespace TelegramSearchBot.Search.Tool {
    internal class ContentQueryBuilder : IQueryBuilder {
        private readonly UnifiedTokenizer _tokenizer;
        private readonly Action<string>? _logAction;

        public ContentQueryBuilder(UnifiedTokenizer tokenizer, Action<string>? logAction = null) {
            _tokenizer = tokenizer;
            _logAction = logAction;
        }

        public BooleanQuery BuildQuery(string query, long groupId, IndexReader reader) {
            var booleanQuery = new BooleanQuery();
            var keywords = TokenizeQuery(query);

            foreach (var keyword in keywords) {
                if (!string.IsNullOrWhiteSpace(keyword)) {
                    var termQuery = new TermQuery(new Term("Content", keyword));
                    booleanQuery.Add(termQuery, Occur.SHOULD);
                }
            }

            return booleanQuery;
        }

        public List<string> TokenizeQuery(string query) {
            var tokens = _tokenizer.SafeTokenize(query);
            if (tokens.Count == 0) {
                _logAction?.Invoke($"ContentQueryBuilder: 查询 \"{query}\" 未能生成有效关键词");
            }
            return tokens;
        }
    }
}
