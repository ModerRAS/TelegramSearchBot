using System;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;

namespace TelegramSearchBot.Search.Tokenizer {
    /// <summary>
    /// 统一分词处理器 - 提供安全的分词逻辑和降级策略。
    /// </summary>
    public class UnifiedTokenizer {
        private readonly Analyzer _analyzer;
        private readonly Action<string>? _logAction;

        public UnifiedTokenizer(Action<string>? logAction = null) {
            _analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            _logAction = logAction;
        }

        public List<string> Tokenize(string text) {
            var keywords = new HashSet<string>();
            try {
                using (var reader = new StringReader(text))
                using (var tokenStream = _analyzer.GetTokenStream(fieldName: null, reader)) {
                    tokenStream.Reset();
                    var termAttribute = tokenStream.GetAttribute<ICharTermAttribute>();
                    while (tokenStream.IncrementToken()) {
                        var keyword = termAttribute.ToString();
                        if (!string.IsNullOrWhiteSpace(keyword)) {
                            keywords.Add(keyword);
                        }
                    }
                }
            } catch (System.Exception ex) {
                _logAction?.Invoke($"分词处理失败: {ex.Message}, Text: {text}");
                keywords.Add(text);
            }

            return keywords.ToList();
        }

        public List<string> SafeTokenize(string text) {
            try {
                return Tokenize(text);
            } catch (System.Exception ex) {
                _logAction?.Invoke($"分词处理失败，使用原始文本: {ex.Message}, Text: {text}");

                return text
                    .Split(new[] { ' ', ',', '.', ';', '，', '。', '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();
            }
        }
    }
}
