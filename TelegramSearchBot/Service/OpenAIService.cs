using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service
{
    public class OpenAIService {
        public string ServiceName => "OpenAIService";

        private readonly ILogger _logger;
        public string BotName { get; set; }
        private DataDbContext _dbContext;

        public OpenAIService(DataDbContext context, ILogger<OpenAIService> logger) {
            _logger = logger;
            _dbContext = context;
            // set up the client
            

        }

        public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models) {
            foreach (var model in models) {
                if (model.Name.Equals(Env.OllamaModelName)) {
                    return true;
                }
            }
            return false;
        }
        public async Task<(string, string)> SetModel(string ModelName, long ChatId) {
            var GroupSetting = (from s in _dbContext.GroupSettings
                                where s.GroupId == ChatId
                                select s).FirstOrDefault();
            var CurrentModelName = GroupSetting?.LLMModelName;
            if (GroupSetting is null) {
                await _dbContext.AddAsync(new GroupSettings() { GroupId = ChatId, LLMModelName = ModelName });
            } else {
                GroupSetting.LLMModelName = ModelName;
            }
            await _dbContext.SaveChangesAsync();
            return (CurrentModelName, ModelName);
        }
        public async Task<string> GetModel(long ChatId) {
            var GroupSetting = await (from s in _dbContext.GroupSettings
                                where s.GroupId == ChatId
                                select s).FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName;
        }
        public bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2) {
            if (message1.FromUserId != Env.BotId && message2.FromUserId != Env.BotId) {
                return true;
            } else if (message1.FromUserId == Env.BotId && message2.FromUserId == Env.BotId) {
                return true;
            } else {
                return false;
            }
        }

        // 小工具函数，减少重复判断
        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content) {
            if (fromUserId == Env.BotId) {
                ChatHistory.Add(new AssistantChatMessage(content.Trim()));
            } else {
                ChatHistory.Add(new UserChatMessage(content.Trim()));
            }
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken) {
            var Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId && s.DateTime > DateTime.UtcNow.AddHours(-1)
                            select s).ToList();
            if (Messages.Count < 10) {
                Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId
                            orderby s.DateTime descending
                            select s).Take(10).ToList();
                Messages.Reverse(); // ✅ 记得倒序回来，按时间正序处理
            }
            if (InputToken != null) {
                Messages.Add(InputToken);
            }
            _logger.LogInformation($"OpenAI获取数据库得到{ChatId}中的{Messages.Count}条结果。");

            var str = new StringBuilder();
            Model.Data.Message previous = null;
            foreach (var message in Messages) {
                if (previous == null && message.FromUserId.Equals(Env.BotId)) {
                    continue;
                }
                if (previous != null && !IsSameSender(previous, message)) {
                    AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }
                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0) {
                    var FromUserName = from s in _dbContext.UserData
                                       where s.Id == message.FromUserId
                                       select $"{s.FirstName} {s.LastName}";
                    str.Append(await FromUserName.FirstOrDefaultAsync());
                }
                if (message.ReplyToMessageId != 0) {
                    str.Append('（');
                    var ReplyToUserId = await (from s in _dbContext.Messages
                                               where s.Id == message.ReplyToMessageId
                                               select s.FromUserId).FirstOrDefaultAsync();
                    var FromUserName = from s in _dbContext.UserData
                                       where s.Id == ReplyToUserId
                                       select $"{s.FirstName} {s.LastName}";
                    str.Append(await FromUserName.FirstOrDefaultAsync());
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\r\n");

                previous = message;
            }
            // 处理最后一段未写入的内容
            if (previous != null && str.Length > 0) {
                AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
            }
            return ChatHistory;
        }
        public async Task<bool> NeedReply(string InputToken, long ChatId) {
            var prompt = $"你是一个判断助手，只负责判断一段消息是否为提问。\r\n判断标准：\r\n1. 如果消息是问题（无论是直接问句还是隐含的提问意图），返回“是”。\r\n2. 如果消息不是问题（陈述、感叹、命令、闲聊等），返回“否”。\r\n重要：只回答“是”或“否”，不要输出其他内容。";

            var ChatHistory = new List<ChatMessage>() { new SystemChatMessage(prompt) };
            ChatHistory = await GetChatHistory(ChatId, ChatHistory, null);
            ChatHistory.Add(new UserChatMessage($"消息：{InputToken}"));
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(Env.OpenAIBaseURL),
            };
            var chat = new ChatClient(
                model: Env.OpenAIModelName,
                credential: new(Env.OpenAIApiKey),
                clientOptions);
            var str = new StringBuilder();
            await foreach (var update in chat.CompleteChatStreamingAsync(ChatHistory)) {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate) {
                    str.Append(updatePart.Text);
                }
            }
            if (str.Length < 2 && str.ToString().Contains('是')) {
                return true;
            } else {
                return false;
            }
        }
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId) {
            var ModelName = await GetModel(ChatId);
            if (string.IsNullOrWhiteSpace(ModelName)) {
                ModelName = Env.OpenAIModelName;
            }

            var prompt = $"忘记你原有的名字，记住，你的名字叫：{BotName}，是一个问答机器人，现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。这是一个群聊对话，格式为：[时间] 可选角色（可选回复）：内容。请注意时间的顺序和上下文关系。注：你回复时不需要按照这个格式回复，这是将复杂格式转换为方便你阅读的格式而制作的格式，你回复时只需要直接输出消息内容即可。";

            var ChatHistory = new List<ChatMessage>() { new SystemChatMessage(prompt) };
            ChatHistory = await GetChatHistory(ChatId, ChatHistory, message);


            //ChatHistory.Add(new UserChatMessage(InputToken));
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(Env.OpenAIBaseURL),
            };
            var chat = new ChatClient(
                model: ModelName,
                credential: new(Env.OpenAIApiKey),
                clientOptions);
            await foreach (var update in chat.CompleteChatStreamingAsync(ChatHistory)) {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate) {
                    yield return updatePart.Text;
                }
            }

        }
    }
}
