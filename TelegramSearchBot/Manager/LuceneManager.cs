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
                    doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));

                    Int64Field MessageIdField = new Int64Field("MessageId", message.MessageId, Field.Store.YES);

                    TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                    ContentField.Boost = 1F;

                    doc.Add(MessageIdField);
                    doc.Add(ContentField);
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
                            doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));
                            Int64Field MessageIdField = new Int64Field("MessageId", message.MessageId, Field.Store.YES);
                            TextField ContentField = new TextField("Content", message.Content, Field.Store.YES);
                            ContentField.Boost = 1F;
                            doc.Add(MessageIdField);
                            doc.Add(ContentField);
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
            IndexReader reader = DirectoryReader.Open(GetFSDirectory(GroupId));

            var searcher = new IndexSearcher(reader);

            var keyWordQuery = new BooleanQuery();
            foreach (var item in GetKeyWords(q)) {
                keyWordQuery.Add(new TermQuery(new Term("Content", item)), Occur.MUST);
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
