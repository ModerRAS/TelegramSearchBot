using System;

namespace TelegramSearchBot.Model
{
    public static class OrleansStreamConstants
    {
        // Stream Namespaces
        public const string RawMessagesStreamNamespace = "RawMessages";
        public const string TextContentStreamNamespace = "TextContent";
        // Add other namespaces as needed, e.g., for processed BiliLink info, etc.

        // Stream IDs (using Guids for uniqueness is good practice, but names can also work if managed carefully)
        // These are illustrative. Actual Guids should be generated or a consistent naming scheme used.

        // Raw Message Streams
        public static readonly Guid RawImageMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawImageMessagesStream"); // Replace with actual Guid
        public static readonly Guid RawVideoMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawVideoMessagesStream"); // Replace with actual Guid
        public static readonly Guid RawAudioMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawAudioMessagesStream"); // Replace with actual Guid
        public static readonly Guid RawTextMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawTextMessagesStream");   // Replace with actual Guid
        public static readonly Guid RawCommandMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawCommandMessagesStream"); // Replace with actual Guid
        public static readonly Guid RawCallbackQueryMessagesStreamId = Guid.Parse("YOUR_GUID_HERE_RawCallbackQueryMessagesStream"); // Replace with actual Guid

        // Central Text Processing Stream
        public static readonly Guid TextContentToProcessStreamId = Guid.Parse("YOUR_GUID_HERE_TextContentToProcessStream"); // Replace with actual Guid

        // It's often better to use string names for streams if Guids are cumbersome to manage,
        // especially if they are derived from message types or other runtime data.
        // Example using string names (these would be used with GetStream<T>(string name, string namespace)):
        public const string RawImageMessagesStreamName = "RawImageMessages";
        public const string RawVideoMessagesStreamName = "RawVideoMessages";
        public const string RawAudioMessagesStreamName = "RawAudioMessages";
        public const string RawTextMessagesStreamName = "RawTextMessages";
        public const string RawCommandMessagesStreamName = "RawCommandMessages";
        public const string RawCallbackQueryMessagesStreamName = "RawCallbackQueryMessages";
        public const string TextContentToProcessStreamName = "TextContentToProcess";
    }
}
