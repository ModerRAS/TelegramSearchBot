using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM {
    /// <summary>
    /// A per-execution turn source: owns the provider-typed history for one agent run and
    /// streams one assistant turn per call. The shared <see cref="Service.AI.LLM.LlmToolLoop"/>
    /// drives the tool-call cycle; transports only translate normalized turns to/from the
    /// provider API. One instance is created per ExecAsync/ResumeFromSnapshot run.
    /// </summary>
    public interface ILlmTurnSource {
        /// <summary>True when the provider supports native tool-calling for this model/protocol.</summary>
        bool SupportsNativeTools { get; }

        /// <summary>
        /// Stream one assistant turn.
        /// Text-protocol sources: <paramref name="userContent"/> is the user message for this turn
        /// (initial input on the first turn, tool feedback afterwards); no tools are attached.
        /// Native sources: <paramref name="userContent"/> is the initial input on the first turn
        /// only; after <see cref="CommitToolResults"/> the next call passes null.
        /// Yields text/thinking deltas, then exactly one <see cref="LlmStreamEvent.TurnCompleted"/>.
        /// </summary>
        IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(string? userContent, CancellationToken cancellationToken);

        /// <summary>
        /// Append the assistant turn to the source's history. Called once per turn after
        /// <see cref="LlmStreamEvent.TurnCompleted"/>. Text-protocol sources append the assistant
        /// message (if non-empty) and update tracked history; native sources append only the
        /// plain-text final assistant message (tool-call turns are committed by
        /// <see cref="CommitToolResults"/>).
        /// </summary>
        void CommitAssistantTurn(string text, string reasoning, bool isFinal);

        /// <summary>
        /// Native mode only: append the assistant tool-call blocks plus the executed results
        /// to the source's history so the next turn sees them.
        /// </summary>
        void CommitToolResults(IReadOnlyList<LlmToolResult> results);

        /// <summary>Serialized history for continuation snapshots.</summary>
        IReadOnlyList<SerializedChatMessage> GetTrackedHistory();
    }
}
