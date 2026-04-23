using System;
using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Search;
using TelegramSearchBot.Search.Interface;
using TelegramSearchBot.Tokenizer.Abstractions;

namespace TelegramSearchBot.Search.Tool {
    internal class ExtQueryBuilder : IQueryBuilder {
        private readonly ITokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly Action<string>? _logAction;

        public ExtQueryBuilder(ITokenizer tokenizer, ExtFieldQueryOptimizer extOptimizer, Action<string>? logAction = null) {
            _tokenizer = tokenizer;
            _extOptimizer = extOptimizer;
            _logAction = logAction;
        }

        public BooleanQuery BuildQuery(string query, long groupId, IndexReader reader) {
            var keywords = TokenizeQuery(query);
            return _extOptimizer.BuildOptimizedExtQuery(keywords, reader, groupId);
        }

        public List<string> TokenizeQuery(string query) {
            var tokens = _tokenizer.SafeTokenize(query).ToList();
            if (tokens.Count == 0) {
                _logAction?.Invoke($"ExtQueryBuilder: 查询 \"{query}\" 未能生成有效关键词");
            }
            return tokens;
        }
    }
}
