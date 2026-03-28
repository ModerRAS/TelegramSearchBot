using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using TelegramSearchBot.Tokenizer.Abstractions;

namespace TelegramSearchBot.Tokenizer.Implementations;

public class SmartChineseTokenizer : ITokenizer {
    private readonly Analyzer _analyzer;
    private readonly Action<string>? _logAction;

    public SmartChineseTokenizer(Action<string>? logAction = null) {
        _analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
        _logAction = logAction;
        Metadata = new TokenizerMetadata("SmartChinese", "Chinese", true);
    }

    public TokenizerMetadata Metadata { get; }

    public IReadOnlyList<string> Tokenize(string text) {
        var keywords = new HashSet<string>();
        try {
            using var reader = new StringReader(text);
            using var tokenStream = _analyzer.GetTokenStream(fieldName: null, reader);
            tokenStream.Reset();
            var termAttribute = tokenStream.GetAttribute<ICharTermAttribute>();
            while (tokenStream.IncrementToken()) {
                var keyword = termAttribute.ToString();
                if (!string.IsNullOrWhiteSpace(keyword)) {
                    keywords.Add(keyword);
                }
            }
        } catch (Exception ex) {
            _logAction?.Invoke($"分词处理失败: {ex.Message}, Text: {text}");
            keywords.Add(text);
        }

        return keywords.ToList();
    }

    public IReadOnlyList<string> SafeTokenize(string text) {
        try {
            return Tokenize(text);
        } catch (Exception ex) {
            _logAction?.Invoke($"分词处理失败，使用原始文本: {ex.Message}, Text: {text}");

            return text
                .Split(new[] { ' ', ',', '.', ';', '，', '。', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();
        }
    }

    public IReadOnlyList<TokenWithOffset> TokenizeWithOffsets(string text) {
        var tokens = new List<TokenWithOffset>();
        try {
            using var reader = new StringReader(text);
            using var tokenStream = _analyzer.GetTokenStream(fieldName: null, reader);
            tokenStream.Reset();
            var termAttribute = tokenStream.GetAttribute<ICharTermAttribute>();
            var offsetAttribute = tokenStream.GetAttribute<Lucene.Net.Analysis.TokenAttributes.IOffsetAttribute>();
            while (tokenStream.IncrementToken()) {
                var term = termAttribute.ToString();
                if (string.IsNullOrWhiteSpace(term)) continue;
                var start = offsetAttribute.StartOffset;
                var end = offsetAttribute.EndOffset;
                if (start >= 0 && end >= start && end <= text.Length) {
                    tokens.Add(new TokenWithOffset(start, end, term));
                }
            }
        } catch (Exception ex) {
            _logAction?.Invoke($"分词处理失败: {ex.Message}, Text: {text}");
            // Fallback: return the whole text as a single token
            if (!string.IsNullOrEmpty(text)) {
                tokens.Add(new TokenWithOffset(0, text.Length, text));
            }
        }
        return tokens;
    }
}
