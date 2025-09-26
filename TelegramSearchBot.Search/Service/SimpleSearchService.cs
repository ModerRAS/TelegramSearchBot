using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using TelegramSearchBot.Search.Model;
using TelegramSearchBot.Search.Tokenizer;
using TelegramSearchBot.Search.Tool;

namespace TelegramSearchBot.Search.Service {
    public class SimpleSearchService {
        private readonly UnifiedTokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly Func<string, Task>? _log;

        public SimpleSearchService(UnifiedTokenizer tokenizer, ExtFieldQueryOptimizer extOptimizer, Func<string, Task>? log) {
            _tokenizer = tokenizer;
            _extOptimizer = extOptimizer;
            _log = log;
        }

        private List<string> GetKeyWords(string query) {
            return _tokenizer.SafeTokenize(query);
        }

        private (Query Query, string[] Terms) ParseSimpleQuery(string query, IndexReader reader) {
            _ = reader; // 保留参数以兼容未来扩展
            _ = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48); // 与原实现保持一致，虽然当前未使用

            var booleanQuery = new BooleanQuery();
            var terms = GetKeyWords(query).ToArray();
            foreach (var term in terms) {
                if (string.IsNullOrWhiteSpace(term)) {
                    continue;
                }

                booleanQuery.Add(new TermQuery(new Term("Content", term)), Occur.SHOULD);
            }

            return (booleanQuery, terms);
        }

        public (int Total, List<MessageDTO> Messages) Search(string query, long groupId, int skip, int take, DirectoryReader reader) {
            var searcher = new IndexSearcher(reader);
            var (luceneQuery, searchTerms) = ParseSimpleQuery(query, reader);

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

            _ = _log?.Invoke($"SimpleSearch完成: GroupId={groupId}, Query={query}, Results={topDocs.TotalHits},耗时={DateTime.Now:HH:mm:ss.fff}");
            return (topDocs.TotalHits, messages);
        }
    }
}
