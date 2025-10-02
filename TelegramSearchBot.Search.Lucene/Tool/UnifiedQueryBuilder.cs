using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace TelegramSearchBot.Search.Lucene.Tool {
    internal class UnifiedQueryBuilder {
        private readonly ContentQueryBuilder _contentBuilder;
        private readonly ExtQueryBuilder _extBuilder;
        private readonly ExtFieldQueryOptimizer _extOptimizer;

        public UnifiedQueryBuilder(ContentQueryBuilder contentBuilder, ExtQueryBuilder extBuilder, ExtFieldQueryOptimizer extOptimizer) {
            _contentBuilder = contentBuilder;
            _extBuilder = extBuilder;
            _extOptimizer = extOptimizer;
        }

        public BooleanQuery BuildUnifiedQuery(List<string> keywords, IndexReader reader, long groupId) {
            var combinedQuery = new BooleanQuery();

            var contentQuery = _contentBuilder.BuildQuery(string.Join(" ", keywords), groupId, reader);
            combinedQuery.Add(contentQuery, Occur.SHOULD);

            var extQuery = _extBuilder.BuildQuery(string.Join(" ", keywords), groupId, reader);
            combinedQuery.Add(extQuery, Occur.SHOULD);

            return combinedQuery;
        }

        public BooleanQuery BuildUnifiedPhraseQuery(List<string> terms, IndexReader reader, long groupId) {
            var combinedQuery = new BooleanQuery();

            var contentPhraseQuery = new PhraseQuery();
            for (int i = 0; i < terms.Count; i++) {
                contentPhraseQuery.Add(new Term("Content", terms[i]), i);
            }
            combinedQuery.Add(contentPhraseQuery, Occur.SHOULD);

            var extPhraseQuery = _extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId);
            combinedQuery.Add(extPhraseQuery, Occur.SHOULD);

            return combinedQuery;
        }
    }
}
