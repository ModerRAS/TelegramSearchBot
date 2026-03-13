using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// Represents a serializable entry in the LLM conversation history.
    /// Used for snapshot persistence when the tool-call iteration limit is reached.
    /// </summary>
    public class SerializedChatMessage {
        /// <summary>
        /// Role: "system", "assistant", "user"
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The text content of the message
        /// </summary>
        public string Content { get; set; }
    }

    /// <summary>
    /// Snapshot of LLM conversation state saved when max tool cycles is reached.
    /// This allows seamless continuation when the user clicks "Continue".
    /// Stored in Redis with TTL for automatic expiry.
    /// </summary>
    public class LlmContinuationSnapshot {
        /// <summary>
        /// Unique identifier for this snapshot
        /// </summary>
        public string SnapshotId { get; set; }

        /// <summary>
        /// The Telegram chat ID where the conversation is happening
        /// </summary>
        public long ChatId { get; set; }

        /// <summary>
        /// The original user message ID (for reply context, int for Telegram API compatibility)
        /// </summary>
        public int OriginalMessageId { get; set; }

        /// <summary>
        /// The user ID who initiated the conversation
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// The LLM model name being used
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// The LLM provider (e.g., "OpenAI", "Ollama", "Gemini")
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// The LLM channel ID being used
        /// </summary>
        public int ChannelId { get; set; }

        /// <summary>
        /// Serialized conversation history (all messages including system, user, assistant, tool results)
        /// </summary>
        public List<SerializedChatMessage> ProviderHistory { get; set; } = new();

        /// <summary>
        /// The last accumulated content that was displayed to the user
        /// </summary>
        public string LastAccumulatedContent { get; set; }

        /// <summary>
        /// Number of tool cycles completed so far
        /// </summary>
        public int CyclesSoFar { get; set; }

        /// <summary>
        /// UTC timestamp when this snapshot was created
        /// </summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether this snapshot is currently being processed (prevents duplicate execution)
        /// </summary>
        public bool InProgress { get; set; }
    }
}
