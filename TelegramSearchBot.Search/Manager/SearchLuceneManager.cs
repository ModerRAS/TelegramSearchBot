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
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Search.Manager
{
    /// <summary>
    /// Lucene索引管理器 - 简化实现版本
    /// 移除SendMessage依赖，专注于核心Lucene功能
    /// 实现ILuceneManager接口
    /// </summary>
    public class SearchLuceneManager : ILuceneManager 
    {
        private readonly string indexPathBase;
        
        public SearchLuceneManager(string indexPathBase = null)
        {
            this.indexPathBase = indexPathBase ?? Path.Combine(AppContext.BaseDirectory, "Index");
        }

        private IndexWriter GetIndexWriter(long groupId)
        {
            var groupIndexPath = Path.Combine(indexPathBase, groupId.ToString());
            var dir = FSDirectory.Open(groupIndexPath);
            var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            return new IndexWriter(dir, indexConfig);
        }

        private IndexReader GetIndexReader(long groupId)
        {
            var groupIndexPath = Path.Combine(indexPathBase, groupId.ToString());
            var dir = FSDirectory.Open(groupIndexPath);
            return DirectoryReader.Open(dir);
        }

        private IndexSearcher GetIndexSearcher(long groupId)
        {
            return new IndexSearcher(GetIndexReader(groupId));
        }

        public async Task WriteDocumentAsync(Message message)
        {
            using (var writer = GetIndexWriter(message.GroupId))
            {
                try
                {
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
                    if (message.MessageExtensions != null)
                    {
                        foreach (var ext in message.MessageExtensions)
                        {
                            doc.Add(new TextField($"Ext_{ext.ExtensionType}", ext.ExtensionData, Field.Store.YES));
                        }
                    }
                    writer.AddDocument(doc);
                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    writer.Commit();
                }
                catch (ArgumentNullException ex)
                {
                    // 简化版本：暂时忽略错误日志，待后续完善
                    Console.WriteLine($"LuceneManager WriteDocumentAsync Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LuceneManager WriteDocumentAsync Unexpected Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 批量写入文档到Lucene索引 - 简化实现
        /// </summary>
        /// <param name="messages">消息列表</param>
        /// <returns>异步任务</returns>
        public async Task WriteDocuments(List<Message> messages)
        {
            if (messages == null || !messages.Any())
            {
                return;
            }

            // 按群组分组处理
            var groupedMessages = messages.GroupBy(m => m.GroupId);
            
            foreach (var group in groupedMessages)
            {
                using (var writer = GetIndexWriter(group.Key))
                {
                    try
                    {
                        foreach (var message in group)
                        {
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
                            if (message.MessageExtensions != null)
                            {
                                foreach (var ext in message.MessageExtensions)
                                {
                                    doc.Add(new TextField($"Ext_{ext.ExtensionType}", ext.ExtensionData, Field.Store.YES));
                                }
                            }
                            writer.AddDocument(doc);
                        }
                        
                        writer.Flush(triggerMerge: true, applyAllDeletes: true);
                        writer.Commit();
                    }
                    catch (ArgumentNullException ex)
                    {
                        // 简化版本：暂时忽略错误日志，待后续完善
                        Console.WriteLine($"LuceneManager WriteDocuments Error: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"LuceneManager WriteDocuments Unexpected Error: {ex.Message}");
                    }
                }
            }
        }

        public async Task<(int, List<Message>)> Search(string keyword, long groupId, int skip = 0, int take = 20)
        {
            try
            {
                var searcher = GetIndexSearcher(groupId);
                // 简化版本：使用TermQuery，暂时不使用QueryParser
                var term = new Term("Content", keyword);
                var query = new TermQuery(term);

                var topDocs = searcher.Search(query, skip + take);
                var results = new List<Message>();

                for (int i = skip; i < Math.Min(topDocs.ScoreDocs.Length, skip + take); i++)
                {
                    var scoreDoc = topDocs.ScoreDocs[i];
                    var doc = searcher.Doc(scoreDoc.Doc);

                    var message = new Message
                    {
                        GroupId = doc.GetField("GroupId")?.GetInt64Value() ?? 0,
                        MessageId = doc.GetField("MessageId")?.GetInt64Value() ?? 0,
                        Content = doc.Get("Content"),
                        DateTime = DateTime.Parse(doc.Get("DateTime")),
                        FromUserId = doc.GetField("FromUserId")?.GetInt64Value() ?? 0,
                        ReplyToUserId = doc.GetField("ReplyToUserId")?.GetInt64Value() ?? 0,
                        ReplyToMessageId = doc.GetField("ReplyToMessageId")?.GetInt64Value() ?? 0
                    };

                    results.Add(message);
                }

                return (topDocs.TotalHits, results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LuceneManager Search Error: {ex.Message}");
                return (0, new List<Message>());
            }
        }

        public async Task<(int, List<Message>)> SearchAll(string keyword, int skip = 0, int take = 20)
        {
            // 简化版本：暂时只支持群组搜索
            // 待完善多群组搜索功能
            return (0, new List<Message>());
        }

        public async Task<(int, List<Message>)> SyntaxSearch(string keyword, long groupId, int skip = 0, int take = 20)
        {
            // 简化版本：暂时委托给普通搜索
            // 待完善语法搜索功能
            return await Search(keyword, groupId, skip, take);
        }

        public async Task<(int, List<Message>)> SyntaxSearchAll(string keyword, int skip = 0, int take = 20)
        {
            // 简化版本：暂时委托给普通搜索
            return await SearchAll(keyword, skip, take);
        }

        public async Task DeleteDocumentAsync(long groupId, long messageId)
        {
            using (var writer = GetIndexWriter(groupId))
            {
                try
                {
                    var term = new Term("MessageId", messageId.ToString());
                    writer.DeleteDocuments(term);
                    writer.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LuceneManager DeleteDocumentAsync Error: {ex.Message}");
                }
            }
        }

        public async Task<bool> IndexExistsAsync(long groupId)
        {
            try
            {
                var groupIndexPath = Path.Combine(indexPathBase, groupId.ToString());
                return System.IO.Directory.Exists(groupIndexPath) && System.IO.Directory.EnumerateFiles(groupIndexPath).Any();
            }
            catch
            {
                return false;
            }
        }
    }
}