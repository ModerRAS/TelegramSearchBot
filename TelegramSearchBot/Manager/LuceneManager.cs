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

        private (BooleanQuery, string[]) ParseQuery(string q, IndexReader reader) {
            var query = new BooleanQuery();
            var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            
            // 处理引号包裹的精确匹配
            var phraseMatches = System.Text.RegularExpressions.Regex.Matches(q, "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match match in phraseMatches) {
                var phraseQuery = new PhraseQuery();
                using (var ts = analyzer.GetTokenStream(null, match.Groups[1].Value)) {
                    ts.Reset();
                    var ct = ts.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
                    int position = 0;
                    while (ts.IncrementToken()) {
                        phraseQuery.Add(new Term("Content", ct.ToString()), position++);
                    }
                }
                query.Add(phraseQuery, Occur.MUST);
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
                
                query.Add(new TermQuery(new Term(field, value)), Occur.MUST);
                q = q.Replace(match.Value, ""); // 移除已处理的字段搜索
            }

            // 处理排除关键词 -keyword
            var excludeMatches = System.Text.RegularExpressions.Regex.Matches(q, @"-([^\s]+)");
            foreach (System.Text.RegularExpressions.Match match in excludeMatches) {
                var term = new Term("Content", match.Groups[1].Value);
                query.Add(new TermQuery(term), Occur.MUST_NOT);
                q = q.Replace(match.Value, ""); // 移除已处理的排除词
            }

            // 处理剩余的关键词
            var remainingTerms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in remainingTerms) {
                if (string.IsNullOrWhiteSpace(term)) continue;
                
                var termQuery = new TermQuery(new Term("Content", term));
                query.Add(termQuery, Occur.SHOULD);
            }

            return (query, remainingTerms);
        }
        public (int, List<Message>) Search(string q, long GroupId, int Skip, int Take) {
            IndexReader reader = DirectoryReader.Open(GetFSDirectory(GroupId));
            var searcher = new IndexSearcher(reader);

            var (query, searchTerms) = ParseQuery(q, reader);
            
            // 添加扩展字段搜索
            var fields = MultiFields.GetIndexedFields(reader);
            foreach (var field in fields) {
                if (field.StartsWith("Ext_")) {
                    var terms = MultiFields.GetTerms(reader, field);
                    if (terms != null) {
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
    }
}
