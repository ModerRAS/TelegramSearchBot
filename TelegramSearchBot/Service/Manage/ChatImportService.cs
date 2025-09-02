using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
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
                    dbMessage.FromUserId = long.TryParse(message.From_Id, out var fromId) ? fromId : 0;
                    dbMessage.ReplyToMessageId = message.ReplyToMessageId ?? 0;

                    // 获取json文件所在目录
                    var jsonDir = Path.GetDirectoryName(filePath);

                    // 处理照片
                    if (!string.IsNullOrEmpty(message.Photo)) {
                        var photoPath = Path.Combine(Env.WorkDir, "Photos", $"{chatExport.Id}");
                        Directory.CreateDirectory(photoPath);
                        var sourcePhotoPath = Path.Combine(jsonDir, message.Photo);
                        var extension = Path.GetExtension(message.Photo) ?? ".jpg";
                        var photoFile = Path.Combine(photoPath, $"{message.Id}{extension}");
                        if (!File.Exists(photoFile)) {
                            File.Copy(sourcePhotoPath, photoFile, true);
                        }
                    }

                    // 处理文件
                    if (!string.IsNullOrEmpty(message.File)) {
                        if (!message.File.StartsWith("(File not included.")) {
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
            if (message.Text_Entities != null && message.Text_Entities.Count > 0) {
                var result = new System.Text.StringBuilder();
                foreach (var entity in message.Text_Entities) {
                    switch (entity.Type) {
                        case "plain":
                            result.Append(entity.Text);
                            break;
                        case "bold":
                            result.Append($"**{entity.Text}**");
                            break;
                        case "italic":
                            result.Append($"*{entity.Text}*");
                            break;
                        case "strikethrough":
                            result.Append($"~~{entity.Text}~~");
                            break;
                        case "spoiler":
                            result.Append($"||{entity.Text}||");
                            break;
                        case "code":
                            result.Append($"`{entity.Text}`");
                            break;
                        case "pre":
                            result.Append($"```{entity.Language ?? ""}\n{entity.Text}\n```");
                            break;
                        case "text_link":
                            result.Append($"[{entity.Text}]({entity.Href})");
                            break;
                        case "mention":
                            result.Append($"[{entity.Text}](tg://user?id={entity.Text.TrimStart('@')})");
                            break;
                        case "bot_command":
                            result.Append($"/{entity.Text.TrimStart('/')}");
                            break;
                        case "email":
                            result.Append($"[{entity.Text}](mailto:{entity.Text})");
                            break;
                        case "hashtag":
                            result.Append($"{entity.Text}");
                            break;
                        case "cashtag":
                            result.Append($"{entity.Text}");
                            break;
                        case "custom_emoji":
                            result.Append($"[{entity.Text}](tg://emoji?id={entity.DocumentId})");
                            break;
                        default:
                            result.Append(entity.Text);
                            break;
                    }
                }
                return result.ToString();
            }
            return string.Empty;
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
