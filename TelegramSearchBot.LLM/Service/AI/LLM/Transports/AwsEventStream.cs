using System.Text;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>Minimal Amazon Event Stream codec (prelude + string headers + payload). Used by Bedrock Converse Stream.</summary>
    internal static class AwsEventStream {
        internal readonly record struct Message(string EventType, string Payload);

        public static byte[] Encode(string eventType, string jsonPayload) {
            var headers = EncodeHeaders(eventType);
            var payload = Encoding.UTF8.GetBytes(jsonPayload);
            var total = 12 + headers.Length + payload.Length + 4;
            using var ms = new MemoryStream();
            WriteInt(ms, total);
            WriteInt(ms, headers.Length);
            var prelude = ms.ToArray();
            WriteInt(ms, unchecked((int)Crc32(prelude)));
            ms.Write(headers);
            ms.Write(payload);
            var withPayload = ms.ToArray();
            WriteInt(ms, unchecked((int)Crc32(withPayload)));
            return ms.ToArray();
        }

        public static List<Message> Decode(ReadOnlySpan<byte> bytes) {
            var messages = new List<Message>();
            var offset = 0;
            while (offset + 12 <= bytes.Length) {
                var total = ReadInt(bytes, offset);
                if (total < 16 || offset + total > bytes.Length) {
                    break;
                }
                var headersLength = ReadInt(bytes, offset + 4);
                var preludeCrc = unchecked((uint)ReadInt(bytes, offset + 8));
                if (Crc32(bytes.Slice(offset, 8)) != preludeCrc) {
                    throw new InvalidOperationException("Bedrock event stream prelude CRC mismatch");
                }
                var headerStart = offset + 12;
                var payloadStart = headerStart + headersLength;
                var payloadLength = total - 12 - headersLength - 4;
                if (headersLength < 0 || payloadLength < 0 || payloadStart + payloadLength + 4 > offset + total) {
                    throw new InvalidOperationException("Bedrock event stream frame is truncated");
                }
                var messageCrc = unchecked((uint)ReadInt(bytes, payloadStart + payloadLength));
                if (Crc32(bytes.Slice(offset, total - 4)) != messageCrc) {
                    throw new InvalidOperationException("Bedrock event stream message CRC mismatch");
                }
                var eventType = ReadEventType(bytes.Slice(headerStart, headersLength));
                var payload = Encoding.UTF8.GetString(bytes.Slice(payloadStart, payloadLength));
                messages.Add(new Message(eventType, payload));
                offset += total;
            }
            return messages;
        }

        private static byte[] EncodeHeaders(string eventType) {
            using var ms = new MemoryStream();
            WriteStringHeader(ms, ":event-type", eventType);
            WriteStringHeader(ms, ":content-type", "application/json");
            WriteStringHeader(ms, ":message-type", "event");
            return ms.ToArray();
        }

        private static void WriteStringHeader(Stream stream, string name, string value) {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var valueBytes = Encoding.UTF8.GetBytes(value);
            stream.WriteByte((byte)nameBytes.Length);
            stream.Write(nameBytes);
            stream.WriteByte(7);
            stream.WriteByte((byte)(valueBytes.Length >> 8));
            stream.WriteByte((byte)(valueBytes.Length & 0xFF));
            stream.Write(valueBytes);
        }

        private static string ReadEventType(ReadOnlySpan<byte> headers) {
            var offset = 0;
            while (offset < headers.Length) {
                var nameLength = headers[offset];
                offset++;
                if (offset + nameLength + 1 > headers.Length) break;
                var name = Encoding.UTF8.GetString(headers.Slice(offset, nameLength));
                offset += nameLength;
                var valueType = headers[offset];
                offset++;
                if (valueType == 7) {
                    if (offset + 2 > headers.Length) break;
                    var valueLength = (headers[offset] << 8) | headers[offset + 1];
                    offset += 2;
                    if (offset + valueLength > headers.Length) break;
                    var value = Encoding.UTF8.GetString(headers.Slice(offset, valueLength));
                    if (name == ":event-type") return value;
                    offset += valueLength;
                } else {
                    offset += ValueLength(headers, ref offset, valueType);
                }
            }
            return string.Empty;
        }

        private static int ValueLength(ReadOnlySpan<byte> headers, ref int offset, byte valueType) {
            return valueType switch {
                0 or 1 => 0,
                2 => 1,
                3 => 2,
                4 => 4,
                5 or 8 => 8,
                6 or 7 => ConsumeCounted(headers, ref offset),
                9 => 16,
                _ => 0
            };
        }

        private static int ConsumeCounted(ReadOnlySpan<byte> headers, ref int offset) {
            if (offset + 2 > headers.Length) return 0;
            var length = (headers[offset] << 8) | headers[offset + 1];
            offset += 2;
            return length;
        }

        private static void WriteInt(Stream stream, int value) {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static int ReadInt(ReadOnlySpan<byte> bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private static uint Crc32(ReadOnlySpan<byte> data) {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data) {
                crc ^= b;
                for (var i = 0; i < 8; i++) {
                    var mask = (uint)-(int)(crc & 1);
                    crc = (crc >> 1) ^ (0xEDB88320 & mask);
                }
            }
            return ~crc;
        }
    }
}
