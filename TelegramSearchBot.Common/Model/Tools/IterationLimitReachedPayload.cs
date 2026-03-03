namespace TelegramSearchBot.Model.Tools {
    /// <summary>
    /// 表示AI迭代次数限制已达，需要用户确认是否继续的特殊消息载体
    /// </summary>
    public class IterationLimitReachedPayload {
        /// <summary>
        /// 特殊标记，用于识别这是一个迭代确认消息
        /// </summary>
        public const string Marker = "___ITERATION_LIMIT_REACHED___";

        /// <summary>
        /// 聊天ID
        /// </summary>
        public long ChatId { get; set; }

        /// <summary>
        /// 原始消息ID（用于回复）
        /// </summary>
        public int OriginalMessageId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 当前迭代次数
        /// </summary>
        public int CurrentCycles { get; set; }

        /// <summary>
        /// 最大迭代次数限制
        /// </summary>
        public int MaxCycles { get; set; }

        /// <summary>
        /// 已经累积的内容（用户需要看到的之前的结果）
        /// </summary>
        public string AccumulatedContent { get; set; }

        public string ToJsonString() {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        public static IterationLimitReachedPayload FromJsonString(string json) {
            return System.Text.Json.JsonSerializer.Deserialize<IterationLimitReachedPayload>(json);
        }
    }
}
