using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Manager {
    public class LuceneManager {
        public void WriteDocument(long GroupId, long MessageId, string Content) {
            using (var writer = GetIndexWriter(GroupId)) {

                Document doc = new Document();
                doc.Add(new Int64Field("GroupId", GroupId, Field.Store.YES));

                Int64Field MessageIdField = new Int64Field("MessageId", MessageId, Field.Store.YES);

                TextField ContentField = new TextField("Content", Content, Field.Store.YES);
                ContentField.Boost = 1F;

                doc.Add(MessageIdField);
                doc.Add(ContentField);
                writer.AddDocument(doc);
                writer.Flush(triggerMerge: true, applyAllDeletes: true);
                writer.Commit();
            }
        }
        public void WriteDocument(Message message) {
            using (var writer = GetIndexWriter(message.GroupId)) {

                Document doc = new Document();
                doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));

                Int64Field MessageIdField = new Int64Field("MessageId", message.MessageId, Field.Store.YES);

                TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                ContentField.Boost = 1F;

                doc.Add(MessageIdField);
                doc.Add(ContentField);
                writer.AddDocument(doc);
                writer.Flush(triggerMerge: true, applyAllDeletes: true);
                writer.Commit();
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
            Parallel.ForEach(dict.Keys.ToList(), e => {
                using (var writer = GetIndexWriter(e)) {
                    foreach (var (message, doc) in from message in dict.GetValueOrDefault(e)
                                                   let doc = new Document()
                                                   select (message, doc)) {
                        doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));
                        Int64Field MessageIdField = new Int64Field("MessageId", message.MessageId, Field.Store.YES);
                        TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                        ContentField.Boost = 1F;
                        doc.Add(MessageIdField);
                        doc.Add(ContentField);
                        writer.AddDocument(doc);
                        writer.Flush(triggerMerge: true, applyAllDeletes: true);
                        writer.Commit();
                    }
                }
            });
            
        }
        private IndexWriter GetIndexWriter(long GroupId) {
            var dir = FSDirectory.Open($"{Env.WorkDir}/Index_Data_{GroupId}");
            Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            IndexWriter writer = new IndexWriter(dir, indexConfig);
            return writer;
        }

        private List<string> GetKeyWords(string q) {
            List<string> keyworkds = new List<string>();
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
                    if (!keyworkds.Contains(item)) {
                        keyworkds.Add(item);
                    }
                }
            }
            return keyworkds;
        }
        public (int, List<Message>) Search(string q, long GroupId, int Skip, int Take) {
            IndexReader reader = DirectoryReader.Open(FSDirectory.Open($"{Env.WorkDir}/Index_Data_{GroupId}"));

            var searcher = new IndexSearcher(reader);

            var keyWordQuery = new BooleanQuery();
            foreach (var item in GetKeyWords(q)) {
                keyWordQuery.Add(new FuzzyQuery(new Term("Content", item)), Occur.SHOULD);
                keyWordQuery.Add(new TermQuery(new Term("Content", item)), Occur.SHOULD);
            }
            var top = searcher.Search(keyWordQuery, Skip + Take, new Sort(new SortField("MessageId", SortFieldType.INT64, true)));
            var total = top.TotalHits;
            var hits = top.ScoreDocs;

            var messages = new List<Message>();
            var id = 0;
            foreach (var hit in hits) {
                if (id++ < Skip) continue;
                var document = searcher.Doc(hit.Doc);
                messages.Add(new Message() {
                    Id = id,
                    MessageId = long.Parse(document.Get("MessageId")),
                    GroupId = long.Parse(document.Get("GroupId")),
                    Content = document.Get("Content")
                });
            }
            return (total, messages);
        }
    }
}
