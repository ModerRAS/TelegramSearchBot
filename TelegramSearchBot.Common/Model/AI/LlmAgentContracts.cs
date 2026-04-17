using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.AI {
    public enum AgentTaskKind {
        Message = 0,
        Continuation = 1
    }

    public enum AgentTaskStatus {
        Pending = 0,
        Running = 1,
        Completed = 2,
        Failed = 3
    }

    public enum AgentChunkType {
        Snapshot = 0,
        Done = 1,
        Error = 2,
        IterationLimitReached = 3
    }

    public sealed class AgentUserSnapshot {
        public long UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public bool? IsPremium { get; set; }
        public bool? IsBot { get; set; }
    }

    public sealed class AgentMessageExtensionSnapshot {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class AgentHistoryMessage {
        public long DataId { get; set; }
        public DateTime DateTime { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public AgentUserSnapshot User { get; set; } = new AgentUserSnapshot();
        public List<AgentMessageExtensionSnapshot> Extensions { get; set; } = [];
    }

    public sealed class AgentModelCapability {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class AgentChannelConfig {
        public int ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public LLMProvider Provider { get; set; }
        public int Parallel { get; set; }
        public int Priority { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public List<AgentModelCapability> Capabilities { get; set; } = [];
    }

    public sealed class AgentExecutionTask {
        public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
        public AgentTaskKind Kind { get; set; } = AgentTaskKind.Message;
        public long ChatId { get; set; }
        public long UserId { get; set; }
        public long MessageId { get; set; }
        public long BotUserId { get; set; }
        public string BotName { get; set; } = string.Empty;
        public string InputMessage { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public int MaxToolCycles { get; set; }
        public AgentChannelConfig Channel { get; set; } = new AgentChannelConfig();
        public List<AgentHistoryMessage> History { get; set; } = [];
        public LlmContinuationSnapshot? ContinuationSnapshot { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class AgentStreamChunk {
        public string TaskId { get; set; } = string.Empty;
        public AgentChunkType Type { get; set; } = AgentChunkType.Snapshot;
        public int Sequence { get; set; }
        public string Content { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public LlmContinuationSnapshot? ContinuationSnapshot { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class TelegramAgentToolTask {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, string> Arguments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public long ChatId { get; set; }
        public long UserId { get; set; }
        public long MessageId { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class TelegramAgentToolResult {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Result { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public long TelegramMessageId { get; set; }
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class AgentSessionInfo {
        public long ChatId { get; set; }
        public int ProcessId { get; set; }
        public int Port { get; set; }
        public string Status { get; set; } = "starting";
        public string CurrentTaskId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastActiveAtUtc { get; set; } = DateTime.UtcNow;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public static class LlmAgentRedisKeys {
        public const string AgentTaskQueue = "AGENT_TASKS";
        public const string AgentTaskDeadLetterQueue = "AGENT_TASKS:DEAD";
        public const string TelegramTaskQueue = "TELEGRAM_TASKS";
        public const string ActiveTaskSet = "AGENT_ACTIVE_TASKS";

        public static string AgentTaskState(string taskId) => $"AGENT_TASK:{taskId}";
        public static string AgentChunks(string taskId) => $"AGENT_CHUNKS:{taskId}";
        public static string AgentChunkIndex(string taskId) => $"AGENT_CHUNK_INDEX:{taskId}";
        public static string AgentSession(long chatId) => $"AGENT_SESSION:{chatId}";
        public static string TelegramResult(string requestId) => $"TELEGRAM_RESULT:{requestId}";
    }
}
