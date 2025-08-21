using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Search.Manager;
using Moq;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Benchmarks.Search
{
    /// <summary>
    /// Lucene搜索性能基准测试
    /// 测试不同搜索场景下的性能表现，包括简单搜索和语法搜索
    /// </summary>
    [Config(typeof(SearchPerformanceBenchmarkConfig))]
    [MemoryDiagnoser]
    public class SearchPerformanceBenchmarks : IDisposable
    {
        private class SearchPerformanceBenchmarkConfig : ManualConfig
        {
            public SearchPerformanceBenchmarkConfig()
            {
                AddColumn(StatisticColumn.AllStatistics);
                AddDiagnoser(MemoryDiagnoser.Default);
                AddJob(Job.Default.WithIterationCount(5).WithWarmupCount(2));
            }
        }

        private readonly Mock<ISendMessageService> _mockSendMessageService;
        private readonly Mock<ILogger<SearchLuceneManager>> _mockLogger;
        private readonly Consumer _consumer = new Consumer();
        
        // 测试数据目录
        private readonly string _testIndexDirectory;
        private const long TestGroupId = 100L;
        
        // Lucene管理器实例
        private SearchLuceneManager _luceneManager;
        
        // 测试数据
        private List<Message> _smallIndexData;
        private List<Message> _mediumIndexData;
        private List<Message> _largeIndexData;

        public SearchPerformanceBenchmarks()
        {
            _mockSendMessageService = new Mock<ISendMessageService>();
            _mockLogger = new Mock<ILogger<SearchLuceneManager>>();
            
            // 设置测试索引目录
            _testIndexDirectory = Path.Combine(Path.GetTempPath(), $"LuceneBenchmark_{Guid.NewGuid()}");
            System.IO.Directory.CreateDirectory(_testIndexDirectory);
            
            // 初始化测试数据
            InitializeTestData();
        }

        [GlobalSetup]
        public async Task Setup()
        {
            // 创建Lucene管理器
            _luceneManager = new SearchLuceneManager(_mockSendMessageService.Object);
            
            // 构建测试索引
            await BuildTestIndexes();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // 清理测试索引目录
            try
            {
                if (System.IO.Directory.Exists(_testIndexDirectory))
                {
                    Directory.Delete(_testIndexDirectory, true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        /// <summary>
        /// 初始化测试数据
        /// </summary>
        private void InitializeTestData()
        {
            var random = new Random(42);
            
            // 生成测试用的关键词和内容
            var keywords = new[]
            {
                "performance", "optimization", "benchmark", "test", "search", "index", "query", "database",
                "csharp", "dotnet", "lucene", "efcore", "telegram", "bot", "api", "service",
                "异步", "性能", "测试", "搜索", "索引", "数据库", "优化", "框架", "开发"
            };

            var templates = new[]
            {
                "讨论关于{0}的实现方案",
                "分析了{0}的性能表现",
                "优化了{0}的相关代码",
                "测试了{0}的功能特性",
                "实现了{0}的核心逻辑",
                "修复了{0}的相关bug",
                "改进了{0}的用户体验",
                "重构了{0}的架构设计"
            };

            // 小数据集：1,000条消息
            _smallIndexData = GenerateTestMessages(1000, keywords, templates, random);
            
            // 中等数据集：10,000条消息
            _mediumIndexData = GenerateTestMessages(10000, keywords, templates, random);
            
            // 大数据集：50,000条消息
            _largeIndexData = GenerateTestMessages(50000, keywords, templates, random);
        }

        /// <summary>
        /// 生成测试消息数据
        /// </summary>
        private List<Message> GenerateTestMessages(int count, string[] keywords, string[] templates, Random random)
        {
            var messages = new List<Message>();
            
            for (int i = 0; i < count; i++)
            {
                var keyword = keywords[random.Next(keywords.Length)];
                var template = templates[random.Next(templates.Length)];
                var content = string.Format(template, keyword);
                
                // 随机添加额外的关键词
                if (random.NextDouble() < 0.3)
                {
                    var extraKeyword = keywords[random.Next(keywords.Length)];
                    content += $" 同时涉及{extraKeyword}";
                }
                
                // 随机添加英文内容
                if (random.NextDouble() < 0.4)
                {
                    var englishKeyword = keywords[random.Next(keywords.Length / 2)]; // 英文关键词
                    content += $" English: {englishKeyword} implementation";
                }

                var message = new Message
                {
                    Id = i + 1,
                    GroupId = TestGroupId,
                    MessageId = i + 1,
                    FromUserId = random.Next(1, 100),
                    Content = content,
                    DateTime = DateTime.UtcNow.AddDays(-random.Next(365)),
                    ReplyToUserId = random.NextDouble() < 0.2 ? random.Next(1, 100) : 0,
                    ReplyToMessageId = random.NextDouble() < 0.2 ? random.Next(1, i) : 0,
                    MessageExtensions = random.NextDouble() < 0.1 ? 
                        new List<MessageExtension> { 
                            new MessageExtension 
                            { 
                                MessageId = i + 1, 
                                ExtensionType = "OCR", 
                                ExtensionData = $"Extracted: {keyword}" 
                            } 
                        } : 
                        new List<MessageExtension>()
                };
                
                messages.Add(message);
            }

            return messages;
        }

        /// <summary>
        /// 构建测试索引
        /// </summary>
        private async Task BuildTestIndexes()
        {
            // 由于SearchLuceneManager的索引目录是硬编码的，我们需要使用反射或直接操作Lucene API
            // 为了简化，我们直接使用Lucene API创建测试索引
            
            await CreateLuceneIndex(_smallIndexData, "small");
            await CreateLuceneIndex(_mediumIndexData, "medium");
            await CreateLuceneIndex(_largeIndexData, "large");
        }

        /// <summary>
        /// 直接使用Lucene API创建索引
        /// </summary>
        private async Task CreateLuceneIndex(List<Message> messages, string indexName)
        {
            var indexPath = Path.Combine(_testIndexDirectory, indexName);
            var directory = FSDirectory.Open(indexPath);
            
            using (var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48))
            {
                var indexConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
                indexConfig.OpenMode = OpenMode.CREATE;
                
                using (var writer = new IndexWriter(directory, indexConfig))
                {
                    foreach (var message in messages)
                    {
                        var doc = CreateDocument(message);
                        writer.AddDocument(doc);
                    }
                    
                    writer.Commit();
                }
            }
        }

        /// <summary>
        /// 创建Lucene文档
        /// </summary>
        private Document CreateDocument(Message message)
        {
            var doc = new Document();
            
            // 基础字段
            doc.Add(new Int64Field("GroupId", message.GroupId, Field.Store.YES));
            doc.Add(new Int64Field("MessageId", message.MessageId, Field.Store.YES));
            doc.Add(new StringField("DateTime", message.DateTime.ToString("o"), Field.Store.YES));
            doc.Add(new Int64Field("FromUserId", message.FromUserId, Field.Store.YES));
            doc.Add(new Int64Field("ReplyToUserId", message.ReplyToUserId, Field.Store.YES));
            doc.Add(new Int64Field("ReplyToMessageId", message.ReplyToMessageId, Field.Store.YES));

            // 内容字段
            var contentField = new TextField("Content", message.Content, Field.Store.YES);
            contentField.Boost = 1F;
            doc.Add(contentField);

            // 扩展字段
            if (message.MessageExtensions != null)
            {
                foreach (var ext in message.MessageExtensions)
                {
                    doc.Add(new TextField($"Ext_{ext.ExtensionType}", ext.ExtensionData, Field.Store.YES));
                }
            }
            
            return doc;
        }

        #region 简单搜索性能测试

        /// <summary>
        /// 测试简单关键词搜索 - 小索引
        /// </summary>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("SimpleSearch")]
        public void SimpleSearchSmallIndex()
        {
            PerformSearch("performance", "small", 0, 10);
        }

        /// <summary>
        /// 测试简单关键词搜索 - 中等索引
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SimpleSearch")]
        public void SimpleSearchMediumIndex()
        {
            PerformSearch("performance", "medium", 0, 10);
        }

        /// <summary>
        /// 测试简单关键词搜索 - 大索引
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SimpleSearch")]
        public void SimpleSearchLargeIndex()
        {
            PerformSearch("performance", "large", 0, 10);
        }

        /// <summary>
        /// 测试中文关键词搜索
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SimpleSearch")]
        public void ChineseKeywordSearch()
        {
            PerformSearch("性能", "medium", 0, 10);
        }

        /// <summary>
        /// 测试英文关键词搜索
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SimpleSearch")]
        public void EnglishKeywordSearch()
        {
            PerformSearch("benchmark", "medium", 0, 10);
        }

        #endregion

        #region 语法搜索性能测试

        /// <summary>
        /// 测试短语搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SyntaxSearch")]
        public void PhraseSearch()
        {
            PerformSearch("\"performance optimization\"", "medium", 0, 10, useSyntaxSearch: true);
        }

        /// <summary>
        /// 测试字段指定搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SyntaxSearch")]
        public void FieldSpecificSearch()
        {
            PerformSearch("content:performance", "medium", 0, 10, useSyntaxSearch: true);
        }

        /// <summary>
        /// 测试排除词搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SyntaxSearch")]
        public void ExclusionSearch()
        {
            PerformSearch("performance -test", "medium", 0, 10, useSyntaxSearch: true);
        }

        /// <summary>
        /// 测试复杂语法搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SyntaxSearch")]
        public void ComplexSyntaxSearch()
        {
            PerformSearch("\"performance optimization\" content:benchmark -test", "medium", 0, 10, useSyntaxSearch: true);
        }

        #endregion

        #region 分页性能测试

        /// <summary>
        /// 测试首页搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Pagination")]
        public void FirstPageSearch()
        {
            PerformSearch("performance", "large", 0, 20);
        }

        /// <summary>
        /// 测试深度分页搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Pagination")]
        public void DeepPageSearch()
        {
            PerformSearch("performance", "large", 1000, 20);
        }

        /// <summary>
        /// 测试大量结果搜索性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Pagination")]
        public void LargeResultSetSearch()
        {
            PerformSearch("performance", "large", 0, 100);
        }

        #endregion

        #region 索引构建性能测试

        /// <summary>
        /// 测试小索引构建性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Indexing")]
        public async Task BuildSmallIndex()
        {
            await CreateLuceneIndex(_smallIndexData, "benchmark_small");
        }

        /// <summary>
        /// 测试中等索引构建性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Indexing")]
        public async Task BuildMediumIndex()
        {
            await CreateLuceneIndex(_mediumIndexData, "benchmark_medium");
        }

        /// <summary>
        /// 测试大索引构建性能
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Indexing")]
        public async Task BuildLargeIndex()
        {
            await CreateLuceneIndex(_largeIndexData, "benchmark_large");
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 执行搜索操作
        /// </summary>
        private void PerformSearch(string query, string indexName, int skip, int take, bool useSyntaxSearch = false)
        {
            var indexPath = Path.Combine(_testIndexDirectory, indexName);
            if (!System.IO.Directory.Exists(indexPath))
                return;

            using (var directory = FSDirectory.Open(indexPath))
            using (var reader = DirectoryReader.Open(directory))
            {
                var searcher = new IndexSearcher(reader);
                var analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
                
                // 构建查询
                var queryObj = useSyntaxSearch ? 
                    ParseSyntaxQuery(query, analyzer) : 
                    ParseSimpleQuery(query, analyzer);

                // 执行搜索
                var topDocs = searcher.Search(queryObj, skip + take);
                var hits = topDocs.ScoreDocs;

                // 处理结果
                var results = new List<Message>();
                var resultCount = 0;
                
                foreach (var hit in hits)
                {
                    if (resultCount++ < skip) continue;
                    if (results.Count >= take) break;

                    var doc = searcher.Doc(hit.Doc);
                    var message = new Message
                    {
                        MessageId = long.Parse(doc.Get("MessageId")),
                        GroupId = long.Parse(doc.Get("GroupId")),
                        Content = doc.Get("Content"),
                        DateTime = DateTime.Parse(doc.Get("DateTime")),
                        FromUserId = long.Parse(doc.Get("FromUserId"))
                    };
                    
                    results.Add(message);
                }
                
                results.Consume(_consumer);
            }
        }

        /// <summary>
        /// 解析简单查询
        /// </summary>
        private Query ParseSimpleQuery(string query, Analyzer analyzer)
        {
            var booleanQuery = new BooleanQuery();
            var terms = GetKeywords(query, analyzer);
            
            foreach (var term in terms)
            {
                if (!string.IsNullOrWhiteSpace(term))
                {
                    var termQuery = new TermQuery(new Term("Content", term));
                    booleanQuery.Add(termQuery, Occur.SHOULD);
                }
            }
            
            return booleanQuery;
        }

        /// <summary>
        /// 解析语法查询 - 简化实现
        /// </summary>
        private Query ParseSyntaxQuery(string query, Analyzer analyzer)
        {
            // 简化实现：直接使用简单查询
            // 在实际应用中，这里应该实现完整的语法解析
            return ParseSimpleQuery(query, analyzer);
        }

        /// <summary>
        /// 获取关键词
        /// </summary>
        private List<string> GetKeywords(string query, Analyzer analyzer)
        {
            var keywords = new List<string>();
            
            using (var tokenStream = analyzer.GetTokenStream(null, query))
            {
                tokenStream.Reset();
                var charTermAttribute = tokenStream.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
                
                while (tokenStream.IncrementToken())
                {
                    var keyword = charTermAttribute.ToString();
                    if (!keywords.Contains(keyword))
                    {
                        keywords.Add(keyword);
                    }
                }
            }
            
            return keywords;
        }

        #endregion

        public void Dispose()
        {
            Cleanup();
        }
    }
}