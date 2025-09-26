using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lucene.Net.Index;
using Lucene.Net.Search;
using TelegramSearchBot.Search.Tokenizer;

namespace TelegramSearchBot.Search.Tool {
    public class PhraseQueryProcessor {
        private static readonly Regex PhraseRegex = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);
        private readonly UnifiedTokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly Action<string>? _logAction;

        public PhraseQueryProcessor(UnifiedTokenizer tokenizer, ExtFieldQueryOptimizer extOptimizer, Action<string>? logAction = null) {
            _tokenizer = tokenizer;
            _extOptimizer = extOptimizer;
            _logAction = logAction;
        }

        public BooleanQuery BuildUnifiedPhraseQuery(List<string> terms, IndexReader reader, long groupId) {
            var combinedQuery = new BooleanQuery();

            var contentPhraseQuery = BuildPhraseQueryForField("Content", terms);
            combinedQuery.Add(contentPhraseQuery, Occur.SHOULD);

            var extPhraseQuery = _extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId);
            combinedQuery.Add(extPhraseQuery, Occur.SHOULD);

            return combinedQuery;
        }

        public (List<BooleanQuery> PhraseQueries, string RemainingQuery) ExtractPhraseQueries(string query, IndexReader reader, long groupId) {
            var phraseQueries = new List<BooleanQuery>();
            var remainingQuery = query;

            foreach (Match match in PhraseRegex.Matches(query)) {
                try {
                    var phraseText = match.Groups[1].Value;
                    var terms = _tokenizer.SafeTokenize(phraseText);
                    if (terms.Count == 0) {
                        continue;
                    }

                    var phraseQuery = new BooleanQuery();
                    phraseQuery.Add(BuildPhraseQueryForField("Content", terms), Occur.SHOULD);
                    phraseQuery.Add(_extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId), Occur.SHOULD);

                    phraseQueries.Add(phraseQuery);
                    _logAction?.Invoke($"提取短语查询: \"{phraseText}\" -> {terms.Count} 个分词");
                    remainingQuery = remainingQuery.Replace(match.Value, string.Empty);
                } catch (System.Exception ex) {
                    _logAction?.Invoke($"处理短语查询失败: {ex.Message}, Phrase: {match.Value}");
                }
            }

            return (phraseQueries, remainingQuery.Trim());
        }

        public bool IsValidPhraseQuery(List<string> terms) {
            return terms != null && terms.Count > 0 && terms.All(t => !string.IsNullOrWhiteSpace(t));
        }

        private static PhraseQuery BuildPhraseQueryForField(string fieldName, List<string> terms) {
            var phraseQuery = new PhraseQuery();
            for (int i = 0; i < terms.Count; i++) {
                phraseQuery.Add(new Term(fieldName, terms[i]), i);
            }
            return phraseQuery;
        }
    }
}
