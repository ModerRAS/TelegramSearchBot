using Xunit;

namespace TelegramSearchBot.Test {
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class McpToolHelperTestCollection {
        public const string Name = "McpToolHelper serial";
    }
}
