using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.ChatExport;
using TelegramSearchBot.Model.Data;
using DataMessage = TelegramSearchBot.Model.Data.Message;
using ExportMessage = TelegramSearchBot.Model.ChatExport.Message;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class ChatImportService : IService {
        public string ServiceName => "ChatImportService";
        private readonly ILogger<ChatImportService> _logger;
        private readonly SendMessage _send;
        private readonly DataDbContext _context;
        private readonly string _importDir;

        public ChatImportService(
            ILogger<ChatImportService> logger,
            SendMessage send,
            DataDbContext context) {
            _logger = logger;
            _send = send;
            _context = context;
            _importDir = Path.Combine(Env.WorkDir, "ChatImport");
            Directory.CreateDirectory(_importDir);
        }

        public async Task ImportFromFileAsync(string filePath) {
            try {
                var json = await File.ReadAllTextAsync(filePath);
                var chatExport = JsonConvert.DeserializeObject<ChatExport>(json);
                ArgumentNullException.ThrowIfNull(chatExport);

                await _send.Log($"开始导入聊天记录: {chatExport.Name}");

                foreach (var message in chatExport.Messages) {
                    // 检查数据库中是否已存在相同MessageId的消息
                    var existingMessages = await _context.Messages
                        .Where(m => m.MessageId == message.Id && m.GroupId == chatExport.Id)
                        .OrderByDescending(m => m.DateTime)
                        .ThenByDescending(m => m.Id)
                        .ToListAsync();

                    var dbMessage = existingMessages.FirstOrDefault();
                    if (dbMessage == null) {
                        dbMessage = new DataMessage();
                    }

                    // 更新消息内容
                    dbMessage.MessageId = message.Id;
                    dbMessage.GroupId = chatExport.Id;
                    dbMessage.DateTime = message.Date;
                    dbMessage.Content = GetMessageText(message);
                    dbMessage.FromUserId = ParseExportUserId(message.From_Id, message.ActorId);
                    dbMessage.ReplyToMessageId = message.ReplyToMessageId ?? 0;

                    // 获取json文件所在目录
                    var jsonDir = Path.GetDirectoryName(filePath);

                    // 处理照片
                    if (HasEmbeddedFile(message.Photo)) {
                        var photoPath = Path.Combine(Env.WorkDir, "Photos", $"{chatExport.Id}");
                        Directory.CreateDirectory(photoPath);
                        var sourcePhotoPath = Path.Combine(jsonDir, message.Photo);
                        var extension = Path.GetExtension(message.Photo) ?? ".jpg";
                        var photoFile = Path.Combine(photoPath, $"{message.Id}{extension}");
                        if (!File.Exists(sourcePhotoPath)) {
                            _logger.LogWarning($"图片不存在: {sourcePhotoPath}");
                        } else if (!File.Exists(photoFile)) {
                            File.Copy(sourcePhotoPath, photoFile, true);
                        }
                    }

                    // 处理文件
                    if (HasEmbeddedFile(message.File)) {
                        var sourceFilePath = Path.Combine(jsonDir, message.File);
                        if (!File.Exists(sourceFilePath)) {
                            _logger.LogWarning($"文件不存在: {sourceFilePath}");
                        } else {
                            var extension = Path.GetExtension(message.File) ?? ".dat";

                            // 根据文件类型保存到不同目录
                            var destPath = IProcessVideo.IsVideo(message.File)
                                ? Path.Combine(Env.WorkDir, "Videos", $"{chatExport.Id}")
                                : IProcessAudio.IsAudio(message.File)
                                    ? Path.Combine(Env.WorkDir, "Audios", $"{chatExport.Id}")
                                    : Path.Combine(Env.WorkDir, "Files", $"{chatExport.Id}");

                            Directory.CreateDirectory(destPath);
                            var destFile = Path.Combine(destPath, $"{message.Id}{extension}");

                            if (!File.Exists(destFile)) {
                                File.Copy(sourceFilePath, destFile, true);
                            }
                        }
                    }

                    await _context.Messages.AddAsync(dbMessage);
                }

                await _context.SaveChangesAsync();
                await _send.Log($"导入完成，共导入{chatExport.Messages.Count}条消息");
            } catch (Exception ex) {
                _logger.LogError(ex, "导入聊天记录失败");
                await _send.Log($"导入失败: {ex.Message}");
            }
        }

        private string GetMessageText(ExportMessage message) {
            var result = new StringBuilder();

            AppendFormattedText(result, message.Text_Entities, message.Text);
            AppendSection(result, BuildCaptionText(message));

            if (message.Sticker != null) {
                AppendSection(result, $"[贴纸: {message.Sticker.Emoji ?? "?"}]");
            }

            if (message.Voice != null) {
                AppendSection(result, "[语音消息]");
            }

            if (message.Video != null) {
                AppendSection(result, "[视频]");
            }

            if (message.VideoNote != null) {
                AppendSection(result, "[视频消息]");
            }

            if (!string.IsNullOrEmpty(message.Poll?.Question)) {
                AppendSection(result, $"[投票: {message.Poll.Question}]");
            }

            if (message.Contact != null) {
                var name = $"{message.Contact.FirstName} {message.Contact.LastName}".Trim();
                AppendSection(result, $"[联系人: {name}]");
            }

            if (message.Location != null) {
                AppendSection(result, "[位置]");
            }

            if (!string.IsNullOrEmpty(message.Action)) {
                var serviceParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(message.Actor)) {
                    serviceParts.Add(message.Actor);
                }

                serviceParts.Add(message.Action);

                if (!string.IsNullOrWhiteSpace(message.Title) &&
                    !string.Equals(message.Title, message.Actor, StringComparison.Ordinal)) {
                    serviceParts.Add(message.Title);
                }

                AppendSection(result, $"[服务消息: {string.Join(" ", serviceParts)}]");
            }

            if (!string.IsNullOrWhiteSpace(message.ForwardedFrom)) {
                AppendSection(result, $"[转发自: {message.ForwardedFrom}]");
            }

            if (!string.IsNullOrWhiteSpace(message.ViaBot)) {
                AppendSection(result, $"[ViaBot: {message.ViaBot}]");
            }

            if (message.Reactions != null && message.Reactions.Count > 0) {
                var reactions = string.Join(", ", message.Reactions
                    .Where(r => !string.IsNullOrWhiteSpace(r.Emoji))
                    .Select(r => $"{r.Emoji}x{r.Count}"));

                if (!string.IsNullOrWhiteSpace(reactions)) {
                    AppendSection(result, $"[回应: {reactions}]");
                }
            }

            return result.ToString();
        }

        private static void AppendFormattedText(StringBuilder builder, List<TextEntity> entities, List<TextItem> items) {
            if (entities != null && entities.Count > 0) {
                foreach (var entity in entities) {
                    builder.Append(FormatEntity(entity));
                }

                return;
            }

            if (items == null || items.Count == 0) {
                return;
            }

            foreach (var item in items) {
                builder.Append(FormatItem(item));
            }
        }

        private static string BuildCaptionText(ExportMessage message) {
            if (message.Caption_Entities != null && message.Caption_Entities.Count > 0) {
                var builder = new StringBuilder();
                foreach (var entity in message.Caption_Entities) {
                    builder.Append(FormatEntity(entity));
                }

                return builder.ToString();
            }

            return message.Caption ?? string.Empty;
        }

        private static string FormatEntity(TextEntity entity) {
            return entity.Type switch {
                "plain" => entity.Text,
                "bold" => $"**{entity.Text}**",
                "italic" => $"*{entity.Text}*",
                "strikethrough" => $"~~{entity.Text}~~",
                "spoiler" => $"||{entity.Text}||",
                "code" => $"`{entity.Text}`",
                "pre" => $"```{entity.Language ?? ""}\n{entity.Text}\n```",
                "text_link" => $"[{entity.Text}]({entity.Href})",
                "mention" => $"[{entity.Text}](tg://user?id={entity.Text.TrimStart('@')})",
                "bot_command" => $"/{entity.Text.TrimStart('/')}",
                "email" => $"[{entity.Text}](mailto:{entity.Text})",
                "custom_emoji" => $"[{entity.Text}](tg://emoji?id={entity.DocumentId})",
                _ => entity.Text
            };
        }

        private static string FormatItem(TextItem item) {
            if (string.IsNullOrEmpty(item.Text)) {
                return string.Empty;
            }

            return item.Type switch {
                "bold" => $"**{item.Text}**",
                "italic" => $"*{item.Text}*",
                "strikethrough" => $"~~{item.Text}~~",
                "spoiler" => $"||{item.Text}||",
                "code" => $"`{item.Text}`",
                "pre" => $"```{item.Language ?? ""}\n{item.Text}\n```",
                "link" or "text_link" when !string.IsNullOrWhiteSpace(item.Href) => $"[{item.Text}]({item.Href})",
                _ => item.Text
            };
        }

        private static void AppendSection(StringBuilder builder, string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            if (builder.Length > 0) {
                builder.AppendLine();
            }

            builder.Append(value);
        }

        private static bool HasEmbeddedFile(string filePath) {
            return !string.IsNullOrWhiteSpace(filePath) &&
                   !filePath.StartsWith("(File not included.", StringComparison.Ordinal);
        }

        private static long ParseExportUserId(params string[] rawIds) {
            foreach (var rawId in rawIds) {
                if (string.IsNullOrWhiteSpace(rawId)) {
                    continue;
                }

                if (long.TryParse(rawId, out var numericId)) {
                    return numericId;
                }

                var match = Regex.Match(rawId, @"-?\d+$");
                if (match.Success && long.TryParse(match.Value, out numericId)) {
                    return numericId;
                }
            }

            return 0;
        }

        public async Task ExecuteAsync(string command) {
            if (command == "导入聊天记录") {
                var files = Directory.GetFiles(_importDir, "*.json");
                foreach (var file in files) {
                    await ImportFromFileAsync(file);
                }
            }
        }
    }
}
