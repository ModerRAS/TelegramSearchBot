using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model.ChatExport {
    public class ChatExport {
        public string Name { get; set; }
        public string Type { get; set; }
        public long Id { get; set; }
        public List<Message> Messages { get; set; }
    }

    public class Message {
        public int Id { get; set; }
        public string Type { get; set; }
        public DateTime Date { get; set; }
        public string Date_Unixtime { get; set; }
        public string From { get; set; }
        public string From_Id { get; set; }
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
}
