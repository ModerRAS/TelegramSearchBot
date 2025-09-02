using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Concurrent;
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
        private readonly UnifiedTokenizer _tokenizer;
        private readonly ExtFieldQueryOptimizer _extOptimizer;
        private readonly PhraseQueryProcessor _phraseProcessor;
        private readonly ContentQueryBuilder _contentBuilder;
        private readonly ExtQueryBuilder _extBuilder;
        private readonly UnifiedQueryBuilder _unifiedBuilder;
        private readonly FieldSpecificationParser _fieldParser;
        
        public LuceneManager(SendMessage Send) {
            this.Send = Send;
            _tokenizer = new UnifiedTokenizer(msg => Send?.Log(msg));
            _extOptimizer = new ExtFieldQueryOptimizer(msg => Send?.Log(msg));
            _phraseProcessor = new PhraseQueryProcessor(_tokenizer, _extOptimizer, msg => Send?.Log(msg));
            
            // 初始化查询构建器
            _contentBuilder = new ContentQueryBuilder(_tokenizer, msg => Send?.Log(msg));
            _extBuilder = new ExtQueryBuilder(_tokenizer, _extOptimizer, msg => Send?.Log(msg));
            _unifiedBuilder = new UnifiedQueryBuilder(_contentBuilder, _extBuilder, _extOptimizer, msg => Send?.Log(msg));
            
            // 初始化字段解析器
            _fieldParser = new FieldSpecificationParser(msg => Send?.Log(msg));
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
                    
                    // 清理Ext字段缓存，确保下次搜索时获取最新字段信息
                    _extOptimizer.ClearCache(message.GroupId);
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
                    
                    // 清理Ext字段缓存，确保下次搜索时获取最新字段信息
                    _extOptimizer.ClearCache(e);
                }
            });
            
        }
        private FSDirectory GetFSDirectory(long GroupId) {
            return FSDirectory.Open(Path.Combine(Env.WorkDir, "Index_Data", $"{GroupId}"));
        }
        
        // 安全获取IndexReader，包含错误处理
        private DirectoryReader SafeGetIndexReader(long groupId)
        {
            try
            {
                var directory = GetFSDirectory(groupId);
                if (!DirectoryReader.IndexExists(directory))
                {
                    Send?.Log($"索引不存在: GroupId={groupId}");
                    return null;
                }
                
                return DirectoryReader.Open(directory);
            }
            catch (Exception ex)
            {
                Send?.Log($"获取索引读取器失败: GroupId={groupId}, Error={ex.Message}");
                return null;
            }
        }
        private IndexWriter GetIndexWriter(long GroupId) {
            var dir = GetFSDirectory(GroupId);
            Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            IndexWriter writer = new IndexWriter(dir, indexConfig);
            return writer;
        }

        // 统一的查询构建接口 - 为Content字段和Ext字段提供一致的查询处理逻辑
        private interface IQueryBuilder
        {
            BooleanQuery BuildQuery(string query, long groupId, IndexReader reader);
            List<string> TokenizeQuery(string query);
        }

        // Content字段查询构建器 - 处理Content字段的查询构建
        private class ContentQueryBuilder : IQueryBuilder
        {
            private readonly UnifiedTokenizer _tokenizer;
            private readonly Action<string> _logAction;

            public ContentQueryBuilder(UnifiedTokenizer tokenizer, Action<string> logAction = null)
            {
                _tokenizer = tokenizer;
                _logAction = logAction;
            }

            public BooleanQuery BuildQuery(string query, long groupId, IndexReader reader)
            {
                var booleanQuery = new BooleanQuery();
                var keywords = TokenizeQuery(query);

                foreach (var keyword in keywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        var termQuery = new TermQuery(new Term("Content", keyword));
                        booleanQuery.Add(termQuery, Occur.SHOULD);
                    }
                }

                return booleanQuery;
            }

            public List<string> TokenizeQuery(string query)
            {
                return _tokenizer.SafeTokenize(query);
            }
        }

        // Ext字段查询构建器 - 处理Ext字段的查询构建
        private class ExtQueryBuilder : IQueryBuilder
        {
            private readonly UnifiedTokenizer _tokenizer;
            private readonly ExtFieldQueryOptimizer _extOptimizer;
            private readonly Action<string> _logAction;

            public ExtQueryBuilder(UnifiedTokenizer tokenizer, ExtFieldQueryOptimizer extOptimizer, Action<string> logAction = null)
            {
                _tokenizer = tokenizer;
                _extOptimizer = extOptimizer;
                _logAction = logAction;
            }

            public BooleanQuery BuildQuery(string query, long groupId, IndexReader reader)
            {
                var keywords = TokenizeQuery(query);
                return _extOptimizer.BuildOptimizedExtQuery(keywords, reader, groupId);
            }

            public List<string> TokenizeQuery(string query)
            {
                return _tokenizer.SafeTokenize(query);
            }
        }

        // 统一查询构建器 - 协调Content和Ext字段的查询构建
        private class UnifiedQueryBuilder
        {
            private readonly ContentQueryBuilder _contentBuilder;
            private readonly ExtQueryBuilder _extBuilder;
            private readonly ExtFieldQueryOptimizer _extOptimizer;
            private readonly Action<string> _logAction;

            public UnifiedQueryBuilder(ContentQueryBuilder contentBuilder, ExtQueryBuilder extBuilder, ExtFieldQueryOptimizer extOptimizer, Action<string> logAction = null)
            {
                _contentBuilder = contentBuilder;
                _extBuilder = extBuilder;
                _extOptimizer = extOptimizer;
                _logAction = logAction;
            }

            // 构建统一的查询（Content + Ext字段）
            public BooleanQuery BuildUnifiedQuery(List<string> keywords, IndexReader reader, long groupId, bool requireAllFields = false)
            {
                var combinedQuery = new BooleanQuery();

                // Content字段查询
                var contentQuery = new BooleanQuery();
                foreach (var keyword in keywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        var termQuery = new TermQuery(new Term("Content", keyword));
                        contentQuery.Add(termQuery, Occur.SHOULD);
                    }
                }
                combinedQuery.Add(contentQuery, Occur.SHOULD);

                // Ext字段查询
                var extQuery = _extBuilder.BuildQuery(string.Join(" ", keywords), groupId, reader);
                combinedQuery.Add(extQuery, Occur.SHOULD);

                return combinedQuery;
            }

            // 构建短语查询的统一版本
            public BooleanQuery BuildUnifiedPhraseQuery(List<string> terms, IndexReader reader, long groupId)
            {
                var combinedQuery = new BooleanQuery();

                // Content字段短语查询
                var contentPhraseQuery = new PhraseQuery();
                for (int i = 0; i < terms.Count; i++)
                {
                    contentPhraseQuery.Add(new Term("Content", terms[i]), i);
                }
                combinedQuery.Add(contentPhraseQuery, Occur.SHOULD);

                // Ext字段短语查询
                var extPhraseQuery = _extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId);
                combinedQuery.Add(extPhraseQuery, Occur.SHOULD);

                return combinedQuery;
            }
        }

        // Ext字段查询优化器 - 优化Ext字段搜索性能，避免每次遍历所有字段
        private class ExtFieldQueryOptimizer
        {
            private readonly ConcurrentDictionary<long, string[]> _fieldCache = new();
            private readonly Action<string> _logAction;

            public ExtFieldQueryOptimizer(Action<string> logAction = null)
            {
                _logAction = logAction;
            }

            // 构建优化的Ext字段查询
            public BooleanQuery BuildOptimizedExtQuery(List<string> keywords, IndexReader reader, long groupId)
            {
                var query = new BooleanQuery();
                var extFields = GetExtFields(reader, groupId);
                
                if (extFields.Length == 0)
                    return query;

                // 优化查询构建：为所有字段和关键词创建一个扁平化的查询结构
                foreach (var keyword in keywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        foreach (var field in extFields)
                        {
                            var termQuery = new TermQuery(new Term(field, keyword));
                            query.Add(termQuery, Occur.SHOULD);
                        }
                    }
                }

                return query;
            }

            // 为短语查询构建优化的Ext字段查询
            public BooleanQuery BuildOptimizedExtPhraseQuery(List<string> terms, IndexReader reader, long groupId)
            {
                var combinedQuery = new BooleanQuery();
                var extFields = GetExtFields(reader, groupId);
                
                if (extFields.Length == 0)
                    return combinedQuery;

                // 为每个Ext字段创建短语查询
                foreach (var field in extFields)
                {
                    var extPhraseQuery = BuildPhraseQueryForField(field, terms);
                    combinedQuery.Add(extPhraseQuery, Occur.SHOULD);
                }

                return combinedQuery;
            }

            // 构建排除关键词的Ext字段查询
            public BooleanQuery BuildOptimizedExtExcludeQuery(List<string> excludeKeywords, IndexReader reader, long groupId)
            {
                var excludeQuery = new BooleanQuery();
                var extFields = GetExtFields(reader, groupId);
                
                if (extFields.Length == 0)
                    return excludeQuery;

                // 为每个排除关键词在所有Ext字段中构建SHOULD查询，然后整体作为MUST_NOT
                foreach (var keyword in excludeKeywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        var keywordExcludeQuery = new BooleanQuery();
                        foreach (var field in extFields)
                        {
                            keywordExcludeQuery.Add(new TermQuery(new Term(field, keyword)), Occur.SHOULD);
                        }
                        // 只有当关键词查询有内容时才添加
                        if (keywordExcludeQuery.Clauses.Count > 0)
                        {
                            excludeQuery.Add(keywordExcludeQuery, Occur.SHOULD);
                        }
                    }
                }

                return excludeQuery;
            }

            // 获取Ext字段列表（带缓存）
            private string[] GetExtFields(IndexReader reader, long groupId)
            {
                return _fieldCache.GetOrAdd(groupId, _ => 
                {
                    try
                    {
                        var fields = MultiFields.GetIndexedFields(reader);
                        var extFields = fields.Where(f => f.StartsWith("Ext_")).ToArray();
                        _logAction?.Invoke($"GroupId {groupId}: 发现 {extFields.Length} 个Ext字段");
                        return extFields;
                    }
                    catch (Exception ex)
                    {
                        _logAction?.Invoke($"获取Ext字段失败: {ex.Message}");
                        return Array.Empty<string>();
                    }
                });
            }

            // 为指定字段构建短语查询
            private PhraseQuery BuildPhraseQueryForField(string fieldName, List<string> terms)
            {
                var phraseQuery = new PhraseQuery();
                for (int i = 0; i < terms.Count; i++)
                {
                    phraseQuery.Add(new Term(fieldName, terms[i]), i);
                }
                return phraseQuery;
            }

            // 清除缓存（用于索引更新时）
            public void ClearCache(long groupId = -1)
            {
                if (groupId == -1)
                {
                    _fieldCache.Clear();
                }
                else
                {
                    _fieldCache.TryRemove(groupId, out _);
                }
            }
        }

        // 字段解析器 - 支持字段指定搜索和字段别名机制
        private class FieldSpecificationParser
        {
            private readonly Action<string> _logAction;

            public FieldSpecificationParser(Action<string> logAction = null)
            {
                _logAction = logAction;
            }

            // 解析字段指定语法
            public FieldSpec ParseFieldSpecification(string fieldSpec)
            {
                if (string.IsNullOrWhiteSpace(fieldSpec))
                    return null;

                var parts = fieldSpec.Split(':', 2);
                if (parts.Length != 2)
                    return null;

                var fieldName = parts[0].Trim();
                var fieldValue = parts[1].Trim();

                // 处理字段别名
                var actualFieldName = ResolveFieldAlias(fieldName);

                return new FieldSpec(actualFieldName, fieldValue);
            }

            // 批量解析字段指定语法
            public List<FieldSpec> ParseFieldSpecifications(string query)
            {
                var fieldSpecs = new List<FieldSpec>();
                var fieldMatches = System.Text.RegularExpressions.Regex.Matches(query, @"(\w+):([^\s]+)");

                foreach (System.Text.RegularExpressions.Match match in fieldMatches)
                {
                    var fieldSpec = ParseFieldSpecification(match.Value);
                    if (fieldSpec != null)
                    {
                        fieldSpecs.Add(fieldSpec);
                    }
                }

                return fieldSpecs;
            }

            // 从查询中提取字段指定部分，返回处理后的查询
            public (List<FieldSpec> FieldSpecs, string RemainingQuery) ExtractFieldSpecifications(string query)
            {
                var fieldSpecs = ParseFieldSpecifications(query);
                var remainingQuery = query;

                foreach (var fieldSpec in fieldSpecs)
                {
                    remainingQuery = remainingQuery.Replace($"{fieldSpec.FieldName}:{fieldSpec.FieldValue}", "");
                }

                return (fieldSpecs, remainingQuery.Trim());
            }

            // 字段别名映射
            private string ResolveFieldAlias(string fieldName)
            {
                return fieldName.ToLowerInvariant() switch
                {
                    "content" => "Content",
                    "ocr" => "Ext_OCR_Result",
                    "asr" => "Ext_ASR_Result",
                    "qr" => "Ext_QR_Result",
                    _ => fieldName // 保持原样，可能是直接指定的字段名
                };
            }

            // 验证字段规范的有效性
            public bool IsValidFieldSpec(FieldSpec fieldSpec)
            {
                if (fieldSpec == null || string.IsNullOrWhiteSpace(fieldSpec.FieldName) || string.IsNullOrWhiteSpace(fieldSpec.FieldValue))
                    return false;

                // 检查字段名是否合法
                if (fieldSpec.FieldName.StartsWith("Ext_") || fieldSpec.FieldName.Equals("Content", StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
        }

        // 字段规范数据模型
        private class FieldSpec
        {
            public string FieldName { get; set; }
            public string FieldValue { get; set; }
            public bool IsExtField => FieldName.StartsWith("Ext_");
            public bool IsContentField => FieldName.Equals("Content", StringComparison.OrdinalIgnoreCase);

            public FieldSpec(string fieldName, string fieldValue)
            {
                FieldName = fieldName;
                FieldValue = fieldValue;
            }

            public override string ToString()
            {
                return $"{FieldName}:{FieldValue}";
            }
        }

        // 短语查询处理器 - 确保短语查询正确处理Content和Ext字段
        private class PhraseQueryProcessor
        {
            private readonly UnifiedTokenizer _tokenizer;
            private readonly ExtFieldQueryOptimizer _extOptimizer;
            private readonly Action<string> _logAction;

            public PhraseQueryProcessor(UnifiedTokenizer tokenizer, ExtFieldQueryOptimizer extOptimizer, Action<string> logAction = null)
            {
                _tokenizer = tokenizer;
                _extOptimizer = extOptimizer;
                _logAction = logAction;
            }

            // 构建统一的短语查询（Content + Ext字段）
            public BooleanQuery BuildUnifiedPhraseQuery(List<string> terms, IndexReader reader, long groupId)
            {
                var combinedQuery = new BooleanQuery();
                
                // Content字段短语查询
                var contentPhraseQuery = BuildPhraseQueryForField("Content", terms);
                combinedQuery.Add(contentPhraseQuery, Occur.SHOULD);
                
                // Ext字段短语查询
                var extPhraseQuery = _extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId);
                combinedQuery.Add(extPhraseQuery, Occur.SHOULD);
                
                return combinedQuery;
            }

            // 从查询字符串中提取和处理短语查询
            public (List<BooleanQuery> PhraseQueries, string RemainingQuery) ExtractPhraseQueries(string query, IndexReader reader = null, long groupId = 0)
            {
                var phraseQueries = new List<BooleanQuery>();
                var remainingQuery = query;

                // 处理引号包裹的精确匹配
                var phraseMatches = System.Text.RegularExpressions.Regex.Matches(query, "\"([^\"]+)\"");
                foreach (System.Text.RegularExpressions.Match match in phraseMatches)
                {
                    try
                    {
                        var phraseText = match.Groups[1].Value;
                        var terms = _tokenizer.SafeTokenize(phraseText);
                        
                        if (terms.Count > 0)
                        {
                            var phraseQuery = new BooleanQuery();
                            // 为Content字段创建短语查询
                            var contentPhraseQuery = BuildPhraseQueryForField("Content", terms);
                            phraseQuery.Add(contentPhraseQuery, Occur.SHOULD);
                            
                            // 为Ext字段创建短语查询
                            var extPhraseQuery = _extOptimizer.BuildOptimizedExtPhraseQuery(terms, reader, groupId);
                            phraseQuery.Add(extPhraseQuery, Occur.SHOULD);
                            
                            phraseQueries.Add(phraseQuery);
                            _logAction?.Invoke($"提取短语查询: \"{phraseText}\" -> {terms.Count} 个分词");
                        }
                        
                        remainingQuery = remainingQuery.Replace(match.Value, "");
                    }
                    catch (Exception ex)
                    {
                        _logAction?.Invoke($"处理短语查询失败: {ex.Message}, Phrase: {match.Value}");
                    }
                }

                return (phraseQueries, remainingQuery.Trim());
            }

            // 为指定字段构建短语查询
            private PhraseQuery BuildPhraseQueryForField(string fieldName, List<string> terms)
            {
                var phraseQuery = new PhraseQuery();
                for (int i = 0; i < terms.Count; i++)
                {
                    phraseQuery.Add(new Term(fieldName, terms[i]), i);
                }
                return phraseQuery;
            }

            // 验证短语查询的有效性
            public bool IsValidPhraseQuery(List<string> terms)
            {
                return terms != null && terms.Count > 0 && terms.All(t => !string.IsNullOrWhiteSpace(t));
            }
        }

        // 统一分词处理器 - 替换原有的GetKeyWords方法，提供更好的错误处理和性能监控
        private class UnifiedTokenizer
        {
            private readonly Analyzer _analyzer;
            private readonly Action<string> _logAction;

            public UnifiedTokenizer(Action<string> logAction = null)
            {
                _analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
                _logAction = logAction;
            }

            public List<string> Tokenize(string text)
            {
                var keywords = new List<string>();
                try
                {
                    using (var ts = _analyzer.GetTokenStream(null, text))
                    {
                        ts.Reset();
                        var ct = ts.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();

                        while (ts.IncrementToken())
                        {
                            var keyword = ct.ToString();
                            if (!keywords.Contains(keyword))
                            {
                                keywords.Add(keyword);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误并返回原始文本作为分词结果
                    _logAction?.Invoke($"分词处理失败: {ex.Message}, Text: {text}");
                    keywords.Add(text);
                }

                return keywords;
            }

            // 安全的分词方法，带有降级处理
            public List<string> SafeTokenize(string text)
            {
                try
                {
                    return Tokenize(text);
                }
                catch (Exception ex)
                {
                    _logAction?.Invoke($"分词处理失败，使用原始文本: {ex.Message}, Text: {text}");
                    
                    // 回退到简单的空格分词
                    return text.Split(new[] { ' ', ',', '.', ';', '，', '。', '；' }, 
                                     StringSplitOptions.RemoveEmptyEntries)
                              .Where(t => !string.IsNullOrWhiteSpace(t))
                              .Distinct()
                              .ToList();
                }
            }
        }

        // 保留原有的GetKeyWords方法作为简化实现，但内部使用UnifiedTokenizer
        // 🔧 代码简化说明：
        // 原本实现：直接在GetKeyWords方法中实现分词逻辑，错误处理不够完善
        // 简化实现：使用UnifiedTokenizer类封装分词逻辑，提供更好的错误处理和降级机制
        // 简化实现的代码文件：TelegramSearchBot/Manager/LuceneManager.cs
        // 简化实现的相关函数方法：GetKeyWords方法
        private List<string> GetKeyWords(string q) {
            var tokenizer = new UnifiedTokenizer(msg => Send?.Log(msg));
            return tokenizer.SafeTokenize(q);
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
        
        // 语法搜索方法 - 新实现，支持字段指定、排除词等语法，使用新的短语查询处理器
        // 🔧 代码简化说明：
        // 原本实现：直接在ParseQuery方法中处理短语查询，逻辑复杂且代码重复
        // 简化实现：使用PhraseQueryProcessor处理短语查询，提供更好的扩展性和维护性
        // 简化实现的代码文件：TelegramSearchBot/Manager/LuceneManager.cs
        // 简化实现的相关函数方法：ParseQuery方法
        private (BooleanQuery, string[]) ParseQuery(string q, IndexReader reader, long groupId) {
            var query = new BooleanQuery();
            Action<string> _logAction = msg => Send?.Log(msg);
            
            // 使用短语查询处理器提取和处理短语查询
            var (phraseQueries, remainingQuery) = _phraseProcessor.ExtractPhraseQueries(q, reader, groupId);
            
            // 添加提取出的短语查询
            foreach (var phraseQuery in phraseQueries)
            {
                query.Add(phraseQuery, Occur.MUST);
            }
            
            // 更新q为剩余的查询字符串
            q = remainingQuery;

            // 使用字段解析器处理字段指定搜索
            var (fieldSpecs, remainingQueryAfterFields) = _fieldParser.ExtractFieldSpecifications(q);
            
            foreach (var fieldSpec in fieldSpecs)
            {
                if (_fieldParser.IsValidFieldSpec(fieldSpec))
                {
                    // 对字段值也进行分词处理
                    var valueTerms = GetKeyWords(fieldSpec.FieldValue);
                    if (valueTerms.Count == 1) {
                        // 如果分词后只有一个词，直接使用
                        query.Add(new TermQuery(new Term(fieldSpec.FieldName, valueTerms[0])), Occur.MUST);
                    } else if (valueTerms.Count > 1) {
                        // 如果分词后有多个词，使用BooleanQuery组合
                        var valueQuery = new BooleanQuery();
                        foreach (var term in valueTerms) {
                            valueQuery.Add(new TermQuery(new Term(fieldSpec.FieldName, term)), Occur.SHOULD);
                        }
                        query.Add(valueQuery, Occur.MUST);
                    }
                    
                    _logAction?.Invoke($"字段指定搜索: {fieldSpec.FieldName}={fieldSpec.FieldValue}");
                }
            }
            
            // 更新q为剩余的查询字符串
            q = remainingQueryAfterFields;

            // 处理排除关键词 -keyword
            var excludeMatches = System.Text.RegularExpressions.Regex.Matches(q, @"-([^\s]+)");
            var excludeTermsList = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in excludeMatches) {
                var excludeValue = match.Groups[1].Value;
                // 对排除关键词也进行分词处理
                var excludeTerms = GetKeyWords(excludeValue);
                excludeTermsList.AddRange(excludeTerms);
                q = q.Replace(match.Value, ""); // 移除已处理的排除词
            }

            // 处理剩余的关键词，使用分词后的关键词
            var remainingTerms = GetKeyWords(q).ToArray();
            
            // 先添加Content字段的常规关键词查询
            foreach (var term in remainingTerms) {
                if (string.IsNullOrWhiteSpace(term)) continue;
                
                var termQuery = new TermQuery(new Term("Content", term));
                query.Add(termQuery, Occur.SHOULD);
            }

            // 添加排除关键词查询（Content字段）
            foreach (var term in excludeTermsList) {
                if (string.IsNullOrWhiteSpace(term)) continue;
                
                var termQuery = new TermQuery(new Term("Content", term));
                query.Add(termQuery, Occur.MUST_NOT);
            }
            
            // 添加排除关键词查询（Ext字段）
            if (excludeTermsList.Count > 0)
            {
                var extExcludeQuery = _extOptimizer.BuildOptimizedExtExcludeQuery(excludeTermsList, reader, groupId);
                if (extExcludeQuery.Clauses.Count > 0)
                {
                    query.Add(extExcludeQuery, Occur.MUST_NOT);
                }
            }

            return (query, remainingTerms);
        }
        // 简单搜索方法 - 搜索Content字段和Ext字段，使用新的优化组件
        // 🔧 代码简化说明：
        // 原本实现：直接在SimpleSearch方法中遍历所有Ext字段，性能较差，代码重复
        // 简化实现：使用ExtFieldQueryOptimizer优化Ext字段查询，提升性能并减少代码重复
        // 简化实现的代码文件：TelegramSearchBot/Manager/LuceneManager.cs
        // 简化实现的相关函数方法：SimpleSearch方法
        public (int, List<Message>) SimpleSearch(string q, long GroupId, int Skip, int Take) {
            try 
            {
                using (var reader = SafeGetIndexReader(GroupId))
                {
                    if (reader == null)
                    {
                        Send?.Log($"SimpleSearch失败: 无法访问索引, GroupId={GroupId}");
                        return (0, new List<Message>());
                    }
                    
                    var searcher = new IndexSearcher(reader);
                    var (query, searchTerms) = ParseSimpleQuery(q, reader);
                    
                    // 使用优化器构建Ext字段查询，替换原有的遍历逻辑
                    if (searchTerms != null && searchTerms.Length > 0)
                    {
                        var extQuery = _extOptimizer.BuildOptimizedExtQuery(searchTerms.ToList(), reader, GroupId);
                        
                        // 将Ext字段查询添加到主查询中
                        if (query is BooleanQuery booleanQuery)
                        {
                            booleanQuery.Add(extQuery, Occur.SHOULD);
                        }
                        else
                        {
                            var newQuery = new BooleanQuery();
                            newQuery.Add(query, Occur.SHOULD);
                            newQuery.Add(extQuery, Occur.SHOULD);
                            query = newQuery;
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
                    
                    Send?.Log($"SimpleSearch完成: GroupId={GroupId}, Query={q}, Results={total},耗时={DateTime.Now:HH:mm:ss.fff}");
                    return (total, messages);
                }
            }
            catch (Exception ex)
            {
                Send?.Log($"SimpleSearch失败: {ex.Message}, GroupId={GroupId}, Query={q}");
                return (0, new List<Message>());
            }
        }
        
        // 语法搜索方法 - 搜索Content字段和Ext字段，使用新的优化组件
        // 🔧 代码简化说明：
        // 原本实现：直接在SyntaxSearch方法中遍历所有Ext字段，性能较差，代码重复
        // 简化实现：使用ExtFieldQueryOptimizer优化Ext字段查询，增强排除关键词处理，提升性能
        // 简化实现的代码文件：TelegramSearchBot/Manager/LuceneManager.cs
        // 简化实现的相关函数方法：SyntaxSearch方法
        public (int, List<Message>) SyntaxSearch(string q, long GroupId, int Skip, int Take) {
            try 
            {
                using (var reader = SafeGetIndexReader(GroupId))
                {
                    if (reader == null)
                    {
                        Send?.Log($"SyntaxSearch失败: 无法访问索引, GroupId={GroupId}");
                        return (0, new List<Message>());
                    }
                    
                    var searcher = new IndexSearcher(reader);

                    var (query, searchTerms) = ParseQuery(q, reader, GroupId);
                    
                    // 使用优化器构建Ext字段查询，替换原有的遍历逻辑
                    if (searchTerms != null && searchTerms.Length > 0)
                    {
                        var extQuery = _extOptimizer.BuildOptimizedExtQuery(searchTerms.ToList(), reader, GroupId);
                        
                        // 将Ext字段查询添加到主查询中
                        if (query is BooleanQuery booleanQuery)
                        {
                            booleanQuery.Add(extQuery, Occur.SHOULD);
                        }
                        else
                        {
                            var newQuery = new BooleanQuery();
                            newQuery.Add(query, Occur.SHOULD);
                            newQuery.Add(extQuery, Occur.SHOULD);
                            query = newQuery;
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
                    
                    Send?.Log($"SyntaxSearch完成: GroupId={GroupId}, Query={q}, Results={total},耗时={DateTime.Now:HH:mm:ss.fff}");
                    return (total, messages);
                }
            }
            catch (Exception ex)
            {
                Send?.Log($"SyntaxSearch失败: {ex.Message}, GroupId={GroupId}, Query={q}");
                return (0, new List<Message>());
            }
        }
        
        // 默认搜索方法 - 保持向后兼容，实际调用简单搜索
        public (int, List<Message>) Search(string q, long GroupId, int Skip, int Take) {
            return SimpleSearch(q, GroupId, Skip, Take);
        }
    }
}
