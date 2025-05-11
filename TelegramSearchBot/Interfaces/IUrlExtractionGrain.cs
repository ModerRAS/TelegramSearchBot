using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for extracting URLs from text content.
    /// </summary>
    public interface IUrlExtractionGrain : IGrainWithGuidKey
    {
        // This grain consumes text content and extracts URLs.
        // It might also perform initial processing like expanding short URLs or fetching titles.
        // If it needs to be explicitly called with text:
        // Task ProcessTextAsync(string text, MessageContext context); 
        // However, the plan indicates it consumes from TextContentToProcess stream.
    }

    // Placeholder for message context if needed for methods, can be expanded later.
    // public class MessageContext
    // {
    //     public long ChatId { get; set; }
    //     public int MessageId { get; set; }
    //     public long UserId { get; set; }
    // }
}
