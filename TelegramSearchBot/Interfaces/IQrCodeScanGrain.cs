using Orleans;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for scanning QR codes from images.
    /// </summary>
    public interface IQrCodeScanGrain : IGrainWithGuidKey
    {
        // Similar to IOcrGrain, this will likely be driven by stream consumption.
        // Methods can be added if direct interaction is needed.
    }
}
