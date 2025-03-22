using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
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
        public async IAsyncEnumerable<string> ExecAsync(string InputToken, long ChatId) {
            var ModelName = await GetModel(ChatId);
            if (string.IsNullOrWhiteSpace(ModelName)) {
                yield break;
            }
            var Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId && s.DateTime > DateTime.UtcNow.AddHours(-1)
                            select s).ToList();
            if (Messages.Count < 10) {
                Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId
                            orderby s.DateTime descending
                            select s).Take(10).ToList();
            }
            
            _logger.LogInformation($"OpenAI获取数据库得到{ChatId}中的{Messages.Count}条结果。");
            var MessagesJson = JsonConvert.SerializeObject(Messages, Formatting.Indented);
            var prompt = $"忘记你原有的名字，记住，你的名字叫：{BotName}，是一个问答机器人，在向你提问之前，我将给你提供以下聊天记录，以供参考,格式为Json格式的列表，其中每一条的聊天记录在Content字段内\n{MessagesJson}";

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(Env.OpenAIBaseURL),
            };
            var chat = new ChatClient(
                model: ModelName,
                credential: new(Env.OpenAIApiKey),
                clientOptions);
            await foreach (var update in chat.CompleteChatStreamingAsync([new SystemChatMessage(prompt), new UserChatMessage(InputToken)])) {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate) {
                    yield return updatePart.Text;
                }
            }

        }
    }
}
