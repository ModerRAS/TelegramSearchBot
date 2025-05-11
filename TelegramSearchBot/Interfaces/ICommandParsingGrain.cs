using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for parsing user commands and their arguments.
    /// </summary>
    public interface ICommandParsingGrain : IGrainWithGuidKey
    {
        // This grain consumes text (raw commands or from TextContentToProcess stream)
        // and parses commands. It might dispatch to other command-specific grains.
        // Example of a direct invocation method:
        // Task ParseAndExecuteCommandAsync(string commandText, MessageContext context);
    }
}
