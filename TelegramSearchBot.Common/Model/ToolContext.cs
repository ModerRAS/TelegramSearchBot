namespace TelegramSearchBot.Model {
    public class ToolContext {
        public long ChatId { get; set; }
        public long UserId { get; set; }
        /// <summary>
        /// The original user message ID, used as default reply target for tool actions (e.g. sending photos).
        /// </summary>
        public long MessageId { get; set; }

        /// <summary>
        /// True when the tool is being executed inside an OS-level sandboxed tool host.
        /// Sandboxed tool hosts are allowed to expose file/process tools to non-admin chats
        /// because host file access is constrained by the sandbox policy.
        /// </summary>
        public bool IsSandboxed { get; set; }

        /// <summary>
        /// Optional sandbox box name used for diagnostics and routing.
        /// </summary>
        public string SandboxBoxName { get; set; } = string.Empty;
    }
}
