using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cn.Smart;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using TelegramSearchBot.Search.Exception;

namespace TelegramSearchBot.Search.Tool {
    public static class SearchHelper {
        /// <summary>
        /// 在 text 中根据 query（中文为主）找到最匹配的 token，并返回一个按 totalLength 裁切的片段；未匹配返回 string.Empty。
        /// 规则：优先完全相同；否则选择与 query token 具有最长公共子串的 text token；并列取先出现者。
        /// 输入非法时抛出 InvalidSearchInputException；分词/处理异常抛出 SearchProcessingException。
        /// </summary>
        public static string FindBestSnippet(string text, string query, int totalLength) {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query) || totalLength <= 0) {
                throw new InvalidSearchInputException("Text/query must be non-empty and totalLength > 0.");
            }

            try {
                // 分词 query -> tokens
                var queryTokens = Tokenize(query);
                if (queryTokens.Count == 0) {
                    // 无有效 query token：按需求未匹配返回空字符串
                    return string.Empty;
                }

                // 分词 text -> tokens 及其原文范围
                var textTokens = TokenizeWithOffsets(text);
                if (textTokens.Count == 0) {
                    return string.Empty;
                }

                // 首先尝试从原始 query 中寻找在 text 中的最长连续子串（优先级最高）
                var rawSub = FindLongestExactSubstringInText(query, text);

                int tokenStart, tokenEndExclusive, tokenLen;
                if (rawSub != null) {
                    tokenStart = rawSub.Value.start;
                    tokenEndExclusive = rawSub.Value.end;
                    tokenLen = tokenEndExclusive - tokenStart;
                } else {
                    // 先尝试完全相同匹配（不区分大小写）
                    var best = FindExactMatch(textTokens, queryTokens);

                    // 若没有完全匹配，则按最长公共子串匹配
                    if (best == null) {
                        best = FindLongestCommonSubstrMatch(textTokens, queryTokens);
                    }

                    if (best == null) {
                        return string.Empty;
                    }

                    // 依据 best 的原文区间裁切片段
                    tokenStart = best.Value.start;
                    tokenEndExclusive = best.Value.end; // exclusive
                    tokenLen = tokenEndExclusive - tokenStart;
                }

                if (tokenLen >= totalLength) {
                    // 片段仅能容纳 token，本身即返回
                    return text.Substring(tokenStart, tokenLen);
                }

                var remain = totalLength - tokenLen;
                var left = remain / 2;
                var right = remain - left;

                var start = Math.Max(0, tokenStart - left);
                var end = Math.Min(text.Length, tokenEndExclusive + right);

                // 如果触碰边界，尝试在另一侧补齐
                var actualLen = end - start;
                if (actualLen < totalLength) {
                    var lacking = totalLength - actualLen;
                    // 尝试向左扩展
                    var extraLeft = Math.Min(lacking, start);
                    start -= extraLeft;
                    lacking -= extraLeft;
                    // 再尝试向右扩展
                    var extraRight = Math.Min(lacking, text.Length - end);
                    end += extraRight;
                }

                if (start < 0) start = 0;
                if (end > text.Length) end = text.Length;

                return text.Substring(start, end - start);
            } catch (InvalidSearchInputException) {
                throw;
            } catch (System.Exception ex) {
                throw new SearchProcessingException("Search processing failed.", ex);
            }
        }

        // 在 text 中查找 query 的最长连续子串；返回首次出现的位置区间
        private static (int start, int end)? FindLongestExactSubstringInText(string query, string text) {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return null;
            // 从最长长度往下尝试
            for (int len = query.Length; len >= 2; len--) {
                for (int i = 0; i + len <= query.Length; i++) {
                    var sub = query.Substring(i, len);
                    if (string.IsNullOrWhiteSpace(sub)) continue;
                    int idx = text.IndexOf(sub, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) {
                        return (idx, idx + sub.Length);
                    }
                }
            }
            return null;
        }

        private static List<string> Tokenize(string input) {
            var tokens = new List<string>();
            using Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            using var tokenStream = analyzer.GetTokenStream("f", input);
            tokenStream.Reset();
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
            while (tokenStream.IncrementToken()) {
                var term = termAttr.ToString();
                if (!string.IsNullOrWhiteSpace(term)) {
                    tokens.Add(term);
                }
            }
            tokenStream.End();
            return tokens;
        }

        private static List<(int start, int end, string term)> TokenizeWithOffsets(string input) {
            var tokens = new List<(int start, int end, string term)>();
            using Analyzer analyzer = new SmartChineseAnalyzer(LuceneVersion.LUCENE_48);
            using var tokenStream = analyzer.GetTokenStream("f", input);
            tokenStream.Reset();
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
            var offsetAttr = tokenStream.GetAttribute<IOffsetAttribute>();
            while (tokenStream.IncrementToken()) {
                var term = termAttr.ToString();
                if (string.IsNullOrWhiteSpace(term)) continue;
                var start = offsetAttr.StartOffset;
                var end = offsetAttr.EndOffset;
                if (start >= 0 && end >= start && end <= input.Length) {
                    tokens.Add((start, end, term));
                }
            }
            tokenStream.End();
            return tokens;
        }

        private static (int start, int end, string term)? FindExactMatch(
            List<(int start, int end, string term)> textTokens,
            List<string> queryTokens) {
            var qset = new HashSet<string>(queryTokens.Select(t => t.ToLowerInvariant()));
            foreach (var t in textTokens) {
                var tt = t.term.ToLowerInvariant();
                if (qset.Contains(tt)) {
                    return t;
                }
            }
            return null;
        }

    private static (int start, int end, string term)? FindLongestCommonSubstrMatch(
            List<(int start, int end, string term)> textTokens,
            List<string> queryTokens) {
            int bestScore = 0;
            (int start, int end, string term)? best = null;
            foreach (var t in textTokens) {
                foreach (var q in queryTokens) {
                    var score = LongestCommonSubstringLength(t.term, q);
            if (score > bestScore && score >= 2) {
                        bestScore = score;
                        best = t;
                    }
                }
            }
            return best;
        }

        private static int LongestCommonSubstringLength(string a, string b) {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            int max = 0;
            for (int i = 1; i <= a.Length; i++) {
                for (int j = 1; j <= b.Length; j++) {
                    if (a[i - 1] == b[j - 1]) {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        if (dp[i, j] > max) max = dp[i, j];
                    } else {
                        dp[i, j] = 0;
                    }
                }
            }
            return max;
        }
    }
}
