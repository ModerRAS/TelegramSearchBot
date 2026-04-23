using System;
using System.Collections.Generic;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// Result object returned alongside the async stream from LLM services.
    /// When iteration limit is reached, the service populates SnapshotData
    /// so the controller can persist it without polluting the message stream.
    /// </summary>
    public class LlmExecutionContext {
        /// <summary>
        /// Set to true when the tool-call loop reaches MaxToolCycles.
        /// </summary>
        public bool IterationLimitReached { get; set; }

        /// <summary>
        /// When IterationLimitReached is true, contains the serialized provider history
        /// and other state needed to resume the conversation.
        /// </summary>
        public LlmContinuationSnapshot SnapshotData { get; set; } = null!;
    }
}
