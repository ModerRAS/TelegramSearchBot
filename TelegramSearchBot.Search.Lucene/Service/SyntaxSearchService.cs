using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using TelegramSearchBot.Search.Lucene.Model;
using TelegramSearchBot.Search.Lucene.Tokenizer;
using TelegramSearchBot.Search.Lucene.Tool;

namespace TelegramSearchBot.Search.Lucene.Service {
    public class SyntaxSearchService {
        private static readonly Regex ExcludeRegex = new Regex(@"-([^\s]+)", RegexOptions.Compiled);

        private readonly PhraseQueryProcessor _phraseProcessor;
        private readonly FieldSpecificationParser _fieldParser;
        private readonly UnifiedTokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly Func<string, Task>? _log;

        public SyntaxSearchService(
            PhraseQueryProcessor phraseProcessor,
            FieldSpecificationParser fieldParser,
            UnifiedTokenizer tokenizer,
            ExtFieldQueryOptimizer extOptimizer,
            Func<string, Task>? log) {
            _phraseProcessor = phraseProcessor;
            _fieldParser = fieldParser;
            _tokenizer = tokenizer;
            _extOptimizer = extOptimizer;
            _log = log;
        }

        private List<string> GetKeyWords(string query) {
            return _tokenizer.SafeTokenize(query);
        }

        private (BooleanQuery Query, string[] Terms) ParseQuery(string query, IndexReader reader, long groupId) {
            var booleanQuery = new BooleanQuery();
            Action<string>? logAction = message => _ = _log?.Invoke(message);

            var (phraseQueries, remainingQuery) = _phraseProcessor.ExtractPhraseQueries(query, reader, groupId);
            foreach (var phraseQuery in phraseQueries) {
                booleanQuery.Add(phraseQuery, Occur.MUST);
            }

            var (fieldSpecs, remainingAfterFields) = _fieldParser.ExtractFieldSpecifications(remainingQuery);
            foreach (var fieldSpec in fieldSpecs) {
                if (!_fieldParser.IsValidFieldSpec(fieldSpec)) {
                    continue;
                }

                var valueTerms = GetKeyWords(fieldSpec.FieldValue);
                if (valueTerms.Count == 1) {
                    booleanQuery.Add(new TermQuery(new Term(fieldSpec.FieldName, valueTerms[0])), Occur.MUST);
                } else if (valueTerms.Count > 1) {
                    var valueQuery = new BooleanQuery();
                    foreach (var term in valueTerms) {
                        valueQuery.Add(new TermQuery(new Term(fieldSpec.FieldName, term)), Occur.SHOULD);
                    }
                    booleanQuery.Add(valueQuery, Occur.MUST);
                }

                logAction?.Invoke($"字段指定搜索: {fieldSpec.FieldName}={fieldSpec.FieldValue}");
            }

            var excludeTermsList = new List<string>();
            var queryWithoutExclude = remainingAfterFields;
            foreach (Match match in ExcludeRegex.Matches(remainingAfterFields)) {
                var excludeValue = match.Groups[1].Value;
                excludeTermsList.AddRange(GetKeyWords(excludeValue));
                queryWithoutExclude = queryWithoutExclude.Replace(match.Value, string.Empty);
            }

            var remainingTerms = GetKeyWords(queryWithoutExclude).ToArray();
            foreach (var term in remainingTerms) {
                if (string.IsNullOrWhiteSpace(term)) {
                    continue;
                }
                booleanQuery.Add(new TermQuery(new Term("Content", term)), Occur.SHOULD);
            }

            foreach (var term in excludeTermsList) {
                if (string.IsNullOrWhiteSpace(term)) {
                    continue;
                }
                booleanQuery.Add(new TermQuery(new Term("Content", term)), Occur.MUST_NOT);
            }

            if (excludeTermsList.Count > 0) {
                var extExcludeQuery = _extOptimizer.BuildOptimizedExtExcludeQuery(excludeTermsList, reader, groupId);
                if (extExcludeQuery.Clauses.Count > 0) {
                    booleanQuery.Add(extExcludeQuery, Occur.MUST_NOT);
                }
            }

            return (booleanQuery, remainingTerms);
        }

        public (int Total, List<MessageDTO> Messages) Search(string query, long groupId, int skip, int take, DirectoryReader reader) {
            var searcher = new IndexSearcher(reader);
            var (luceneQuery, searchTerms) = ParseQuery(query, reader, groupId);

            if (searchTerms != null && searchTerms.Length > 0) {
                var extQuery = _extOptimizer.BuildOptimizedExtQuery(searchTerms.ToList(), reader, groupId);
                if (luceneQuery is BooleanQuery booleanQuery) {
                    booleanQuery.Add(extQuery, Occur.SHOULD);
                } else {
                    var unifiedQuery = new BooleanQuery();
                    unifiedQuery.Add(luceneQuery, Occur.SHOULD);
                    unifiedQuery.Add(extQuery, Occur.SHOULD);
                    luceneQuery = unifiedQuery;
                }
            }

            var topDocs = searcher.Search(luceneQuery, skip + take, new Sort(new SortField("MessageId", SortFieldType.INT64, true)));
            var messages = new List<MessageDTO>();
            var index = 0;
            foreach (var hit in topDocs.ScoreDocs) {
                if (index++ < skip) {
                    continue;
                }
                if (index > skip + take) {
                    break;
                }

                var document = searcher.Doc(hit.Doc);
                var mapped = DocumentMessageMapper.Map(document);
                if (mapped != null) {
                    messages.Add(mapped);
                }
            }

            _ = _log?.Invoke($"SyntaxSearch完成: GroupId={groupId}, Query={query}, Results={topDocs.TotalHits},耗时={DateTime.Now:HH:mm:ss.fff}");
            return (topDocs.TotalHits, messages);
        }
    }
}
