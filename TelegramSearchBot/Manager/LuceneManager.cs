using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Manager
{
    public class LuceneManager {
        private SendMessage Send;
        public LuceneManager(SendMessage Send) {
            this.Send = Send;
        }
        public async Task WriteDocumentAsync(Message message) {
            using (var writer = GetIndexWriter(message.GroupId)) {
                try {
                    Document doc = new Document();
                    // 基础字段
                    doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));
                    doc.Add(new Int64Field("MessageId", message.MessageId, Field.Store.YES));
                    doc.Add(new StringField("DateTime", message.DateTime.ToString("o"), Field.Store.YES));
                    doc.Add(new Int64Field("FromUserId", message.FromUserId, Field.Store.YES));
                    doc.Add(new Int64Field("ReplyToUserId", message.ReplyToUserId, Field.Store.YES));
                    doc.Add(new Int64Field("ReplyToMessageId", message.ReplyToMessageId, Field.Store.YES));

                    // 内容字段
                    TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                    ContentField.Boost = 1F;
                    doc.Add(ContentField);

                    // 扩展字段
                    if (message.MessageExtensions != null) {
                        foreach (var ext in message.MessageExtensions) {
                            doc.Add(new TextField($"Ext_{ext.Name}", ext.Value, Field.Store.YES));
                        }
                    }
                    writer.AddDocument(doc);
                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    writer.Commit();
                } catch (ArgumentNullException ex) {
                    await Send.Log(ex.Message);
                    await Send.Log($"{message.GroupId},{message.MessageId},{message.Content}");
                }
            }
        }
        public void WriteDocuments(IEnumerable<Message> messages) {
            var dict = new Dictionary<long, List<Message>>();
            foreach(var e in messages) {
                if (dict.ContainsKey(e.GroupId)) {
#pragma warning disable CS8602 // 解引用可能出现空引用。实际上不会
                    dict.GetValueOrDefault(e.GroupId).Add(e);
#pragma warning restore CS8602 // 解引用可能出现空引用。
                } else {
                    var list = new List<Message>();
                    list.Add(e);
                    dict.Add(e.GroupId, list);
                }
            }
            Parallel.ForEach(dict.Keys.ToList(), async e => {
                using (var writer = GetIndexWriter(e)) {
                    foreach ((Message message, Document doc) in from message in dict.GetValueOrDefault(e)
                                                   let doc = new Document()
                                                   select (message, doc)) {
                        if (string.IsNullOrEmpty(message.Content)) {
                            continue;
                        }
                        try {
                            // 基础字段
                            doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));
                            doc.Add(new Int64Field("MessageId", message.MessageId, Field.Store.YES));
                            doc.Add(new StringField("DateTime", message.DateTime.ToString("o"), Field.Store.YES));
                            doc.Add(new Int64Field("FromUserId", message.FromUserId, Field.Store.YES));
                            doc.Add(new Int64Field("ReplyToUserId", message.ReplyToUserId, Field.Store.YES));
                            doc.Add(new Int64Field("ReplyToMessageId", message.ReplyToMessageId, Field.Store.YES));

                            // 内容字段
                            TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                            ContentField.Boost = 1F;
                            doc.Add(ContentField);

                            // 扩展字段
                            if (message.MessageExtensions != null) {
                                foreach (var ext in message.MessageExtensions) {
                                    doc.Add(new TextField($"Ext_{ext.Name}", ext.Value, Field.Store.YES));
                                }
                            }
                            writer.AddDocument(doc);
                        } catch (ArgumentNullException ex) {
                            await Send.Log(ex.Message);
                            await Send.Log($"{message.GroupId},{message.MessageId},{message.Content}");
                        }
                        
                    }
                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    writer.Commit();
                }
            });
            
        }
        private FSDirectory GetFSDirectory(long GroupId) {
            return FSDirectory.Open(Path.Combine(Env.WorkDir, "Index_Data", $"{GroupId}"));
        }
        private IndexWriter GetIndexWriter(long GroupId) {
            var dir = GetFSDirectory(GroupId);
            Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            IndexWriter writer = new IndexWriter(dir, indexConfig);
            return writer;
        }

        private List<string> GetKeyWords(string q) {
            List<string> keywords = new List<string>();
            Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            using (var ts = analyzer.GetTokenStream(null, q)) {
                ts.Reset();
                var ct = ts.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();

                while (ts.IncrementToken()) {
                    StringBuilder keyword = new StringBuilder();
                    for (int i = 0; i < ct.Length; i++) {
                        keyword.Append(ct.Buffer[i]);
                    }
                    string item = keyword.ToString();
                    if (!keywords.Contains(item)) {
                        keywords.Add(item);
                    }
                }
            }
            return keywords;
        }

        // 简单搜索方法 - 搜索Content字段和Ext字段
        private (Query, string[]) ParseSimpleQuery(string q, IndexReader reader) {
            var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var query = new BooleanQuery();
            
            // 处理搜索词，使用分词后的关键词
            var terms = GetKeyWords(q).ToArray();
            foreach (var term in terms) {
                if (string.IsNullOrWhiteSpace(term)) continue;
                
                var termQuery = new TermQuery(new Term("Content", term));
                query.Add(termQuery, Occur.SHOULD);
            }

            return (query, terms);
        }
        
        // 语法搜索方法 - 新实现，支持字段指定、排除词等语法
        private (BooleanQuery, string[]) ParseQuery(string q, IndexReader reader) {
            var query = new BooleanQuery();
            var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            
            // 处理引号包裹的精确匹配
            var phraseMatches = System.Text.RegularExpressions.Regex.Matches(q, "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match match in phraseMatches) {
                var terms = new List<string>(); // 存储分词后的术语
                
                using (var ts = analyzer.GetTokenStream(null, match.Groups[1].Value)) {
                    ts.Reset();
                    var ct = ts.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
                    while (ts.IncrementToken()) {
                        terms.Add(ct.ToString());
                    }
                }
                
                // 为Content字段创建短语查询
                var contentPhraseQuery = new PhraseQuery();
                for (int i = 0; i < terms.Count; i++) {
                    contentPhraseQuery.Add(new Term("Content", terms[i]), i);
                }
                
                // 创建组合查询，包含Content字段的短语查询
                var combinedQuery = new BooleanQuery();
                combinedQuery.Add(contentPhraseQuery, Occur.SHOULD);
                
                // 为Ext字段创建短语查询（在SyntaxSearch方法中会实际添加到查询中）
                query.Add(combinedQuery, Occur.MUST);
                q = q.Replace(match.Value, ""); // 移除已处理的短语
            }

            // 处理字段指定搜索 field:value
            var fieldMatches = System.Text.RegularExpressions.Regex.Matches(q, @"(\w+):([^\s]+)");
            foreach (System.Text.RegularExpressions.Match match in fieldMatches) {
                var field = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                
                if (field.Equals("content", StringComparison.OrdinalIgnoreCase)) {
                    field = "Content";
                }
                
                // 对字段值也进行分词处理
                var valueTerms = GetKeyWords(value);
                if (valueTerms.Count == 1) {
                    // 如果分词后只有一个词，直接使用
                    query.Add(new TermQuery(new Term(field, valueTerms[0])), Occur.MUST);
                } else if (valueTerms.Count > 1) {
                    // 如果分词后有多个词，使用BooleanQuery组合
                    var valueQuery = new BooleanQuery();
                    foreach (var term in valueTerms) {
                        valueQuery.Add(new TermQuery(new Term(field, term)), Occur.SHOULD);
                    }
                    query.Add(valueQuery, Occur.MUST);
                }
                q = q.Replace(match.Value, ""); // 移除已处理的字段搜索
            }

            // 处理排除关键词 -keyword
            var excludeMatches = System.Text.RegularExpressions.Regex.Matches(q, @"-([^\s]+)");
            foreach (System.Text.RegularExpressions.Match match in excludeMatches) {
                var excludeValue = match.Groups[1].Value;
                // 对排除关键词也进行分词处理
                var excludeTerms = GetKeyWords(excludeValue);
                foreach (var term in excludeTerms) {
                    var termQuery = new TermQuery(new Term("Content", term));
                    query.Add(termQuery, Occur.MUST_NOT);
                }
                q = q.Replace(match.Value, ""); // 移除已处理的排除词
            }

            // 处理剩余的关键词，使用分词后的关键词
            var remainingTerms = GetKeyWords(q).ToArray();
            foreach (var term in remainingTerms) {
                if (string.IsNullOrWhiteSpace(term)) continue;
                
                var termQuery = new TermQuery(new Term("Content", term));
                query.Add(termQuery, Occur.SHOULD);
            }

            return (query, remainingTerms);
        }
        // 简单搜索方法 - 搜索Content字段和Ext字段，不支持语法
        public (int, List<Message>) SimpleSearch(string q, long GroupId, int Skip, int Take) {
            IndexReader reader = DirectoryReader.Open(GetFSDirectory(GroupId));
            var searcher = new IndexSearcher(reader);

            var (query, searchTerms) = ParseSimpleQuery(q, reader);
            
            // 添加扩展字段搜索（简单版本）
            var fields = MultiFields.GetIndexedFields(reader);
            foreach (var field in fields) {
                if (field.StartsWith("Ext_")) {
                    // 检查searchTerms是否有内容
                    if (searchTerms != null && searchTerms.Length > 0) {
                        var extQuery = new BooleanQuery();
                        foreach (var term in searchTerms) {
                            if (!string.IsNullOrWhiteSpace(term)) {
                                extQuery.Add(new TermQuery(new Term(field, term)), Occur.SHOULD);
                            }
                        }
                        // 将扩展字段查询添加到主查询中
                        if (query is BooleanQuery booleanQuery) {
                            booleanQuery.Add(extQuery, Occur.SHOULD);
                        } else {
                            // 如果不是BooleanQuery，创建一个新的BooleanQuery
                            var newQuery = new BooleanQuery();
                            newQuery.Add(query, Occur.SHOULD);
                            newQuery.Add(extQuery, Occur.SHOULD);
                            query = newQuery;
                        }
                    }
                }
            }

            var top = searcher.Search(query, Skip + Take, new Sort(new SortField("MessageId", SortFieldType.INT64, true)));
            var total = top.TotalHits;
            var hits = top.ScoreDocs;

            var messages = new List<Message>();
            var id = 0;
            foreach (var hit in hits) {
                if (id++ < Skip) continue;
                var document = searcher.Doc(hit.Doc);
                var message = new Message() {
                    Id = id,
                    MessageId = long.Parse(document.Get("MessageId")),
                    GroupId = long.Parse(document.Get("GroupId")),
                    Content = document.Get("Content")
                };

                // 安全解析可能缺失的字段
                if (document.Get("DateTime") != null) {
                    message.DateTime = DateTime.Parse(document.Get("DateTime"));
                }
                if (document.Get("FromUserId") != null) {
                    message.FromUserId = long.Parse(document.Get("FromUserId"));
                }
                if (document.Get("ReplyToUserId") != null) {
                    message.ReplyToUserId = long.Parse(document.Get("ReplyToUserId"));
                }
                if (document.Get("ReplyToMessageId") != null) {
                    message.ReplyToMessageId = long.Parse(document.Get("ReplyToMessageId"));
                }

                // 获取扩展字段
                var extensions = new List<MessageExtension>();
                foreach (var field in document.Fields) {
                    if (field.Name.StartsWith("Ext_")) {
                        extensions.Add(new MessageExtension {
                            Name = field.Name.Substring(4),
                            Value = field.GetStringValue()
                        });
                    }
                }
                if (extensions.Any()) {
                    message.MessageExtensions = extensions;
                }

                messages.Add(message);
            }
            return (total, messages);
        }
        
        // 语法搜索方法 - 搜索Content字段和Ext字段，支持字段指定、排除词等语法
        public (int, List<Message>) SyntaxSearch(string q, long GroupId, int Skip, int Take) {
            IndexReader reader = DirectoryReader.Open(GetFSDirectory(GroupId));
            var searcher = new IndexSearcher(reader);

            var (query, searchTerms) = ParseQuery(q, reader);
            
            // 添加扩展字段搜索
            var fields = MultiFields.GetIndexedFields(reader);
            foreach (var field in fields) {
                if (field.StartsWith("Ext_")) {
                    // 检查searchTerms是否有内容，而不是检查字段中的术语
                    if (searchTerms != null && searchTerms.Length > 0) {
                        var extQuery = new BooleanQuery();
                        foreach (var term in searchTerms) {
                            if (!string.IsNullOrWhiteSpace(term)) {
                                extQuery.Add(new TermQuery(new Term(field, term)), Occur.SHOULD);
                            }
                        }
                        query.Add(extQuery, Occur.SHOULD);
                    }
                }
            }

            var top = searcher.Search(query, Skip + Take, new Sort(new SortField("MessageId", SortFieldType.INT64, true)));
            var total = top.TotalHits;
            var hits = top.ScoreDocs;

            var messages = new List<Message>();
            var id = 0;
            foreach (var hit in hits) {
                if (id++ < Skip) continue;
                var document = searcher.Doc(hit.Doc);
                var message = new Message() {
                    Id = id,
                    MessageId = long.Parse(document.Get("MessageId")),
                    GroupId = long.Parse(document.Get("GroupId")),
                    Content = document.Get("Content")
                };

                // 安全解析可能缺失的字段
                if (document.Get("DateTime") != null) {
                    message.DateTime = DateTime.Parse(document.Get("DateTime"));
                }
                if (document.Get("FromUserId") != null) {
                    message.FromUserId = long.Parse(document.Get("FromUserId"));
                }
                if (document.Get("ReplyToUserId") != null) {
                    message.ReplyToUserId = long.Parse(document.Get("ReplyToUserId"));
                }
                if (document.Get("ReplyToMessageId") != null) {
                    message.ReplyToMessageId = long.Parse(document.Get("ReplyToMessageId"));
                }

                // 获取扩展字段
                var extensions = new List<MessageExtension>();
                foreach (var field in document.Fields) {
                    if (field.Name.StartsWith("Ext_")) {
                        extensions.Add(new MessageExtension {
                            Name = field.Name.Substring(4),
                            Value = field.GetStringValue()
                        });
                    }
                }
                if (extensions.Any()) {
                    message.MessageExtensions = extensions;
                }

                messages.Add(message);
            }
            return (total, messages);
        }
        
        // 默认搜索方法 - 保持向后兼容，实际调用简单搜索
        public (int, List<Message>) Search(string q, long GroupId, int Skip, int Take) {
            return SimpleSearch(q, GroupId, Skip, Take);
        }
    }
}
