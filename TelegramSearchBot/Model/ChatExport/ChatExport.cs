using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramSearchBot.Model.ChatExport {
    public class ChatExport {
        public string Name { get; set; }
        public string Type { get; set; }
        public long Id { get; set; }
        public List<Message> Messages { get; set; }
        [JsonProperty("messages_count")]
        public int? MessagesCount { get; set; }
    }

    public class Message {
        public int Id { get; set; }
        public string Type { get; set; }
        public DateTime Date { get; set; }
        public string Date_Unixtime { get; set; }
        public string From { get; set; }
        [JsonProperty("from_id")]
        public string From_Id { get; set; }
        [JsonProperty("actor_id")]
        public string ActorId { get; set; }
        public string Title { get; set; }
        [JsonConverter(typeof(TextItemListConverter))]
        public List<TextItem> Text { get; set; }
        public List<TextEntity> Text_Entities { get; set; }
        public string Edited { get; set; }
        [JsonProperty("edited_unixtime")]
        public string EditedUnixtime { get; set; }
        [JsonProperty("reply_to_message_id")]
        public int? ReplyToMessageId { get; set; }
        public string Photo { get; set; }
        [JsonProperty("photo_file_size")]
        public int? PhotoFileSize { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string File { get; set; }
        [JsonProperty("file_name")]
        public string FileName { get; set; }
        [JsonProperty("file_size")]
        public int? FileSize { get; set; }
        [JsonProperty("media_type")]
        public string MediaType { get; set; }
        [JsonProperty("mime_type")]
        public string MimeType { get; set; }
        [JsonProperty("duration_seconds")]
        public int? DurationSeconds { get; set; }
        public string Video { get; set; }
        [JsonProperty("video_file_size")]
        public int? VideoFileSize { get; set; }
        public string Voice { get; set; }
        [JsonProperty("voice_file_size")]
        public int? VoiceFileSize { get; set; }
        [JsonProperty("video_note")]
        public string VideoNote { get; set; }
        public StickerInfo Sticker { get; set; }
        public string Location { get; set; }
        public ContactInfo Contact { get; set; }
        public PollInfo Poll { get; set; }
        public string Game { get; set; }
        public string Dice { get; set; }
        [JsonProperty("forwarded_from")]
        public string ForwardedFrom { get; set; }
        [JsonProperty("saved_messages")]
        public string SavedMessages { get; set; }
        public string Actor { get; set; }
        public string Action { get; set; }
        [JsonProperty("action_type")]
        public string ActionType { get; set; }
        public List<ReactionInfo> Reactions { get; set; }
        [JsonProperty("via_bot")]
        public string ViaBot { get; set; }
        public string Caption { get; set; }
        [JsonProperty("caption_entities")]
        public List<TextEntity> Caption_Entities { get; set; }
    }

    public class TextItem {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Href { get; set; }
        public string Language { get; set; }
    }

    public class TextEntity {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Href { get; set; }
        public string Language { get; set; }
        [JsonProperty("document_id")]
        public string DocumentId { get; set; }
    }

    public class StickerInfo {
        [JsonProperty("emoji")]
        public string Emoji { get; set; }
        [JsonProperty("sticker_set_id")]
        public string StickerSetId { get; set; }
    }

    public class ContactInfo {
        [JsonProperty("phone_number")]
        public string PhoneNumber { get; set; }
        [JsonProperty("first_name")]
        public string FirstName { get; set; }
        [JsonProperty("last_name")]
        public string LastName { get; set; }
    }

    public class PollInfo {
        public string Question { get; set; }
        public List<PollOption> Options { get; set; }
        [JsonProperty("total_voters")]
        public int? TotalVoters { get; set; }
    }

    public class PollOption {
        public string Text { get; set; }
        public int Voters { get; set; }
    }

    public class ReactionInfo {
        public string Type { get; set; }
        public string Emoji { get; set; }
        [JsonProperty("document_id")]
        public string DocumentId { get; set; }
        public int Count { get; set; }
    }

    public class TextItemListConverter : JsonConverter {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) {
            return objectType == typeof(List<TextItem>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return new List<TextItem>();
            }

            var token = JToken.Load(reader);

            if (token.Type == JTokenType.String) {
                return new List<TextItem> {
                    new() {
                        Type = "plain",
                        Text = token.Value<string>() ?? string.Empty
                    }
                };
            }

            if (token.Type == JTokenType.Object) {
                return new List<TextItem> {
                    DeserializeTextItem(token, serializer)
                };
            }

            if (token.Type != JTokenType.Array) {
                return new List<TextItem>();
            }

            var items = new List<TextItem>();
            foreach (var itemToken in token.Children()) {
                items.Add(DeserializeTextItem(itemToken, serializer));
            }

            return items;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { }

        private static TextItem DeserializeTextItem(JToken token, JsonSerializer serializer) {
            if (token.Type == JTokenType.String) {
                return new TextItem {
                    Type = "plain",
                    Text = token.Value<string>() ?? string.Empty
                };
            }

            return token.ToObject<TextItem>(serializer) ?? new TextItem {
                Type = "plain",
                Text = token.ToString(Formatting.None)
            };
        }
    }
}
