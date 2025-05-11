using System;
using Orleans; // Required for [GenerateSerializer] and [Id] attributes if using Orleans code generator

namespace TelegramSearchBot.Model
{
    // If you plan to use Orleans's code generator for serializers, 
    // you would typically mark this class with [GenerateSerializer]
    // and its properties with [Id(n)].
    // For now, let's define it as a plain C# class.
    // If System.Text.Json is used (via UseSystemTextJson()), Orleans can often serialize POCOs without attributes.

    /// <summary>
    /// A generic wrapper for messages flowing through Orleans Streams.
    /// Contains metadata and the actual payload.
    /// </summary>
    /// <typeparam name="T">The type of the payload.</typeparam>
    // [GenerateSerializer] // Uncomment if using Orleans code generator
    public class StreamMessage<T>
    {
        // [Id(0)] // Uncomment if using Orleans code generator
        public Guid MessageGuid { get; set; } // Unique identifier for this stream message instance

        // [Id(1)]
        public long OriginalMessageId { get; set; } // ID of the original message (e.g., from Telegram)

        // [Id(2)]
        public long ChatId { get; set; }

        // [Id(3)]
        public long UserId { get; set; } // User who sent the original message

        // [Id(4)]
        public DateTime Timestamp { get; set; }

        // [Id(5)]
        public string Source { get; set; } // e.g., "TelegramUpdate", "OcrResult", "AsrResult"

        // [Id(6)]
        public T Payload { get; set; }

        public StreamMessage(T payload, long originalMessageId, long chatId, long userId, string source)
        {
            MessageGuid = Guid.NewGuid();
            Payload = payload;
            OriginalMessageId = originalMessageId;
            ChatId = chatId;
            UserId = userId;
            Source = source;
            Timestamp = DateTime.UtcNow;
        }

        // Parameterless constructor might be needed for some serializers or Orleans internals
        public StreamMessage() { } 
    }
}
