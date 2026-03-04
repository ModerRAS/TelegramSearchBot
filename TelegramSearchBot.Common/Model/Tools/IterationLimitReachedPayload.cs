namespace TelegramSearchBot.Model.Tools {
    /// <summary>
    /// 当 LLM 服务的 tool-call 循环达到 MaxToolCycles 上限时，
    /// 会在最后一次 yield 的累积内容末尾追加 Marker。
    /// 调用方（Controller）通过包装器检测此标记，将其剥离后保留原始内容，
    /// 并弹出 InlineButton 让用户选择是否继续迭代。
    /// </summary>
    public static class IterationLimitReachedPayload {
        /// <summary>
        /// 追加在累积内容末尾的特殊标记
        /// </summary>
        public const string Marker = "\n___ITERATION_LIMIT_REACHED___";

        /// <summary>
        /// 检查流 yield 的字符串是否以迭代限制标记结尾
        /// </summary>
        public static bool IsIterationLimitMessage(string content) {
            return content != null && content.EndsWith(Marker);
        }

        /// <summary>
        /// 在累积内容末尾追加标记
        /// </summary>
        public static string AppendMarker(string accumulatedContent) {
            return (accumulatedContent ?? string.Empty) + Marker;
        }

        /// <summary>
        /// 从带标记的字符串中提取原始内容
        /// </summary>
        public static string StripMarker(string content) {
            if (content != null && content.EndsWith(Marker)) {
                return content.Substring(0, content.Length - Marker.Length);
            }
            return content ?? string.Empty;
        }
    }
}
