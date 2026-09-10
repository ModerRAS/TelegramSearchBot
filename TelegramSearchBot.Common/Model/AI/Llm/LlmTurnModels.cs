using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Model.AI {
    /// <summary>Events streamed by a transport turn. Always terminates with TurnCompleted.</summary>
    public abstract record LlmStreamEvent {
        public sealed record TextDelta(string Delta) : LlmStreamEvent;
        public sealed record ThinkingDelta(string Delta) : LlmStreamEvent;
        public sealed record TurnCompleted(LlmTurnResult Turn) : LlmStreamEvent;
    }
}
