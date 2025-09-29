using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using TelegramSearchBot.Common;
using TelegramSearchBot.Search.Model;
using TelegramSearchBot.Search.Service;
using TelegramSearchBot.Search.Tokenizer;

namespace TelegramSearchBot.Search.Tool {
    public class LuceneManager {
        private readonly Func<string, Task> _log;
        private readonly UnifiedTokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly PhraseQueryProcessor _phraseProcessor;
        private readonly FieldSpecificationParser _fieldParser;
        private readonly SimpleSearchService _simpleSearchService;
        private readonly SyntaxSearchService _syntaxSearchService;

        public LuceneManager(Func<string, Task> log) {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tokenizer = new UnifiedTokenizer(LogFireAndForget);
            _extOptimizer = new ExtFieldQueryOptimizer(LogFireAndForget);
            _phraseProcessor = new PhraseQueryProcessor(_tokenizer, _extOptimizer, LogFireAndForget);
            _fieldParser = new FieldSpecificationParser(LogFireAndForget);
            _simpleSearchService = new SimpleSearchService(_tokenizer, _extOptimizer, _log);
            _syntaxSearchService = new SyntaxSearchService(_phraseProcessor, _fieldParser, _tokenizer, _extOptimizer, _log);
        }

        private void LogFireAndForget(string message) {
            _ = _log(message);
        }

        private Task LogAsync(string message) {
            return _log(message);
        }

        public async Task WriteDocumentAsync(MessageDTO message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            using (var writer = GetIndexWriter(message.GroupId)) {
                try {
                    var document = BuildDocument(message);
                    writer.AddDocument(document);
                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    writer.Commit();

                    _extOptimizer.ClearCache(message.GroupId);
                } catch (ArgumentNullException ex) {
                    await LogAsync(ex.Message);
                    await LogAsync($"{message.GroupId},{message.MessageId},{message.Content}");
                }
            }
        }

        public void WriteDocuments(IEnumerable<MessageDTO> messages) {
            if (messages == null) {
                throw new ArgumentNullException(nameof(messages));
            }

            var grouped = new Dictionary<long, List<MessageDTO>>();
            foreach (var message in messages) {
                if (!grouped.TryGetValue(message.GroupId, out var list)) {
                    list = new List<MessageDTO>();
                    grouped[message.GroupId] = list;
                }
                list.Add(message);
            }

            Parallel.ForEach(grouped, kvp => {
                var groupId = kvp.Key;
                var groupMessages = kvp.Value;

                using (var writer = GetIndexWriter(groupId)) {
                    foreach (var message in groupMessages) {
                        if (string.IsNullOrEmpty(message.Content)) {
                            continue;
                        }

                        try {
                            var document = BuildDocument(message);
                            writer.AddDocument(document);
                        } catch (ArgumentNullException ex) {
                            LogFireAndForget(ex.Message);
                            LogFireAndForget($"{message.GroupId},{message.MessageId},{message.Content}");
                        }
                    }

                    writer.Flush(triggerMerge: true, applyAllDeletes: true);
                    writer.Commit();
                    _extOptimizer.ClearCache(groupId);
                }
            });
        }

        private static Document BuildDocument(MessageDTO message) {
            var document = new Document {
                new Int64Field("Id", message.Id, Field.Store.YES),
                new Int64Field("GroupId", message.GroupId, Field.Store.YES),
                new Int64Field("MessageId", message.MessageId, Field.Store.YES),
                new StringField("DateTime", message.DateTime.ToString("o"), Field.Store.YES),
                new Int64Field("FromUserId", message.FromUserId, Field.Store.YES),
                new Int64Field("ReplyToUserId", message.ReplyToUserId, Field.Store.YES),
                new Int64Field("ReplyToMessageId", message.ReplyToMessageId, Field.Store.YES)
            };

            var content = message.Content ?? string.Empty;
            var contentField = new TextField("Content", content, Field.Store.YES) {
                Boost = 1F
            };
            document.Add(contentField);

            if (message.MessageExtensions != null) {
                foreach (var ext in message.MessageExtensions) {
                    document.Add(new TextField($"Ext_{ext.Name}", ext.Value, Field.Store.YES));
                }
            }

            return document;
        }

        private FSDirectory GetFSDirectory(long groupId) {
            var indexPath = Path.Combine(Env.WorkDir, "Index_Data", $"{groupId}");
            System.IO.Directory.CreateDirectory(indexPath);
            return FSDirectory.Open(indexPath);
        }

        private IndexWriter GetIndexWriter(long groupId) {
            var directory = GetFSDirectory(groupId);
            var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };
            return new IndexWriter(directory, config);
        }

        private DirectoryReader? SafeGetIndexReader(long groupId) {
            try {
                var directory = GetFSDirectory(groupId);
                if (!DirectoryReader.IndexExists(directory)) {
                    return null;
                }

                return DirectoryReader.Open(directory);
            } catch (System.Exception ex) {
                LogFireAndForget($"获取索引读取器失败: {ex.Message}, GroupId={groupId}");
                return null;
            }
        }

        public (int, List<MessageDTO>) SimpleSearch(string query, long groupId, int skip, int take) {
            try {
                using var reader = SafeGetIndexReader(groupId);
                if (reader == null) {
                    LogFireAndForget($"SimpleSearch失败: 无法访问索引, GroupId={groupId}");
                    return (0, new List<MessageDTO>());
                }

                var (total, messages) = _simpleSearchService.Search(query, groupId, skip, take, reader);
                return (total, messages);
            } catch (System.Exception ex) {
                LogFireAndForget($"SimpleSearch失败: {ex.Message}, GroupId={groupId}, Query={query}");
                return (0, new List<MessageDTO>());
            }
        }

        public (int, List<MessageDTO>) SyntaxSearch(string query, long groupId, int skip, int take) {
            try {
                using var reader = SafeGetIndexReader(groupId);
                if (reader == null) {
                    LogFireAndForget($"SyntaxSearch失败: 无法访问索引, GroupId={groupId}");
                    return (0, new List<MessageDTO>());
                }

                var (total, messages) = _syntaxSearchService.Search(query, groupId, skip, take, reader);
                return (total, messages);
            } catch (System.Exception ex) {
                LogFireAndForget($"SyntaxSearch失败: {ex.Message}, GroupId={groupId}, Query={query}");
                return (0, new List<MessageDTO>());
            }
        }

        public (int, List<MessageDTO>) Search(string query, long groupId, int skip, int take) {
            return SimpleSearch(query, groupId, skip, take);
        }
    }
}
