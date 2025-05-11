using Orleans;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for performing OCR on images.
    /// </summary>
    public interface IOcrGrain : IGrainWithGuidKey
    {
        // Define methods for OCR processing if needed, 
        // or it can be a marker interface if all logic is handled via stream consumption.
        // For now, let's assume it might have a method to trigger processing if not purely stream-driven,
        // or to get status. However, the plan describes it consuming from a stream.
        // Let's keep it simple as per the plan's focus on stream processing.
    }
}
