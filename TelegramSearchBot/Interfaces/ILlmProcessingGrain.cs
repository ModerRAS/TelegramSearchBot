using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for processing text content with a Large Language Model (LLM).
    /// </summary>
    public interface ILlmProcessingGrain : IGrainWithGuidKey
    {
        // This grain consumes text content (possibly based on triggers) and interacts with an LLM.
        // If direct invocation is needed:
        // Task<string> ProcessWithLlmAsync(string text, MessageContext context);
        // As per plan, it consumes from TextContentToProcess stream.
    }
}
