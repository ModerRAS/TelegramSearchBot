using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.Json;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service.Common; // For ChatContextProvider
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    public class OpenAIService : IService, ILLMService {
        public string ServiceName => "OpenAIService";

        private readonly ILogger<OpenAIService> _logger;
        public string BotName { get; set; }
        private DataDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _availableToolsPromptPart;

        public OpenAIService(DataDbContext context, ILogger<OpenAIService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _dbContext = context;
            _serviceProvider = serviceProvider;

            // Initialize McpToolHelper. Pass the service's logger or create a specific one if needed.
            // Pass null for IServiceProvider if DI is not used for tool instances, McpToolHelper will try Activator.CreateInstance.
            McpToolHelper.Initialize(_serviceProvider, _logger); // Using OpenAIService's logger for McpToolHelper

            // Register tools and generate the prompt part once.
            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
        }

        public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models)
        {
            foreach (var model in models)
            {
                if (model.Name.Equals(Env.OllamaModelName))
                {
                    return true;
                }
            }
            return false;
        }
        public async Task<(string, string)> SetModel(string ModelName, long ChatId)
        {
            var GroupSetting = (from s in _dbContext.GroupSettings
                                where s.GroupId == ChatId
                                select s).FirstOrDefault();
            var CurrentModelName = GroupSetting?.LLMModelName;
            if (GroupSetting is null)
            {
                await _dbContext.AddAsync(new GroupSettings() { GroupId = ChatId, LLMModelName = ModelName });
            }
            else
            {
                GroupSetting.LLMModelName = ModelName;
            }
            await _dbContext.SaveChangesAsync();
            return (CurrentModelName, ModelName);
        }
        public async Task<string> GetModel(long ChatId)
        {
            var GroupSetting = await (from s in _dbContext.GroupSettings
                                      where s.GroupId == ChatId
                                      select s).FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName;
        }
        // This method is needed again for message merging
        public bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2)
        {
            if (message1 == null || message2 == null) return false; // Guard against nulls

            // Treat non-bot users as the same sender role ("User")
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            
            return msg1IsUser == msg2IsUser; // Both users or both bots count as same sender role
        }

        // 小工具函数，减少重复判断 - This helper is needed again
        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content)
        {
            if (fromUserId == Env.BotId)
            {
                ChatHistory.Add(new AssistantChatMessage(content.Trim()));
            }
            else
            {
                ChatHistory.Add(new UserChatMessage(content.Trim()));
            }
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken)
        {
            var Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId && s.DateTime > DateTime.UtcNow.AddHours(-1)
                            select s).ToList();
            if (Messages.Count < 10)
            {
                Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId
                            orderby s.DateTime descending
                            select s).Take(10).ToList();
                Messages.Reverse(); // ✅ 记得倒序回来，按时间正序处理
            }
            if (InputToken != null)
            {
                Messages.Add(InputToken);
            }
            _logger.LogInformation($"OpenAI获取数据库得到{ChatId}中的{Messages.Count}条结果。");

            // Restore merging logic
            var str = new StringBuilder();
            Model.Data.Message previous = null;
            foreach (var message in Messages)
            {
                // Skip initial bot messages if they are the very first in the history segment
                 if (previous == null && !ChatHistory.Any(ch => ch is UserChatMessage || ch is AssistantChatMessage) && message.FromUserId.Equals(Env.BotId))
                 {
                     previous = message; // Still need to track it as previous for next iteration's IsSameSender check
                     continue;
                 }

                // If the sender role changes, add the accumulated block for the previous sender
                if (previous != null && !IsSameSender(previous, message))
                {
                    AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }

                // Append current message details to the buffer
                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0)
                {
                    var fromUserName = await (from s in _dbContext.UserData
                                              where s.Id == message.FromUserId
                                              select $"{s.FirstName} {s.LastName}").FirstOrDefaultAsync();
                    str.Append(fromUserName ?? $"User({message.FromUserId})");
                }
                else
                {
                     str.Append("System/Unknown"); // Or BotName
                }

                if (message.ReplyToMessageId != 0)
                {
                    str.Append('（');
                    var replyToUserId = await (from s in _dbContext.Messages
                                               where s.Id == message.ReplyToMessageId
                                               select s.FromUserId).FirstOrDefaultAsync();
                    var replyToUserName = await (from s in _dbContext.UserData
                                                 where s.Id == replyToUserId
                                                 select $"{s.FirstName} {s.LastName}").FirstOrDefaultAsync();
                    str.Append($"Reply to {replyToUserName ?? $"User({replyToUserId})"}");
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n"); // Use newline to separate messages within a block

                previous = message; // Update previous message
            }
            // Add the last accumulated block after the loop finishes
            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
            }
            return ChatHistory;
        }

        public async Task<bool> NeedReply(string InputToken, long ChatId)
        {
            var prompt = $"你是一个判断助手，只负责判断一段消息是否为提问。\r\n判断标准：\r\n1. 如果消息是问题（无论是直接问句还是隐含的提问意图），返回“是”。\r\n2. 如果消息不是问题（陈述、感叹、命令、闲聊等），返回“否”。\r\n重要：只回答“是”或“否”，不要输出其他内容。";

            var ChatHistory = new List<ChatMessage>() { new SystemChatMessage(prompt) };
            ChatHistory = await GetChatHistory(ChatId, ChatHistory, null);
            ChatHistory.Add(new UserChatMessage($"消息：{InputToken}"));
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(Env.OpenAIBaseURL),
            };
            var chat = new ChatClient(
                model: Env.OpenAIModelName,
                credential: new(Env.OpenAIApiKey),
                clientOptions);
            var str = new StringBuilder();
            await foreach (var update in chat.CompleteChatStreamingAsync(ChatHistory))
            {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate)
                {
                    str.Append(updatePart.Text);
                }
            }
            if (str.Length < 2 && str.ToString().Contains('是'))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = Env.OpenAIModelName;
            }

            var systemPrompt = $"你的名字是 {BotName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。群聊Id为:{ChatId}\n\n" +
                               $"你正在参与一个群聊。历史消息的格式为：[时间] 用户名 (可选回复对象)：内容。请仔细理解上下文。\n\n" +
                               $"你的核心任务是协助用户。为此，你可以调用外部工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                               $"{_availableToolsPromptPart}\n\n" +
                               $"如果你判断需要使用上述列表中的某个工具，你的回复必须严格遵循以下XML格式，并且不包含任何其他文本（不要在XML前后添加任何说明或聊天内容）：\n" +
                               $"<tool_name>\n" + // Or the LLM might use <tool name="tool_name"><parameters>...</parameters></tool>
                               $"  <parameter1_name>value1</parameter1_name>\n" +
                               $"  <parameter2_name>value2</parameter2_name>\n" +
                               $"  ...\n" +
                               $"</tool_name>\n" +
                               $"或者\n" +
                               $"<tool name=\"tool_name\">\n" +
                               $"  <parameters>\n" +
                               $"    <parameter1_name>value1</parameter1_name>\n" +
                               $"  </parameters>\n" +
                               $"</tool>\n" +
                               $"(请将 'tool_name' 和参数替换为所选工具的实际名称和值。确保XML格式正确无误。)\n\n" +
                               "重要提示：如果你调用一个工具（特别是搜索类工具）后没有找到你需要的信息，或者结果不理想，你可以尝试以下操作：\n" +
                               "1. 修改你的查询参数（例如，使用更宽泛或更具体的关键词，尝试不同的搜索选项等），然后再次调用同一个工具。\n" +
                               "2. 如果多次尝试仍不理想，或者你认为其他工具可能更合适，可以尝试调用其他可用工具。\n" +
                               "3. 在进行多次尝试时，建议在思考过程中记录并调整你的策略。\n" +
                               "如果你认为已经获得了足够的信息，或者不需要再使用工具，请继续下一步。\n\n" +
                               $"在决定是否使用工具时，请仔细分析用户的请求。如果不需要工具，或者工具执行完毕后，请直接以自然语言回复用户。\n" +
                               $"当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";

            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(channel.Gateway) };
            var chat = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            List<ChatMessage> chatHistory = new List<ChatMessage>() { new SystemChatMessage(systemPrompt) };
            chatHistory = await GetChatHistory(ChatId, chatHistory, message); // Add historical and current user message

            ChatContextProvider.SetCurrentChatId(ChatId); // Set ChatId for the current async context
            try
            {
                int maxToolCycles = 5;
                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    var llmResponseAccumulator = new StringBuilder();
                    // Get response from LLM based on current history
                    await foreach (var update in chat.CompleteChatStreamingAsync(chatHistory))
                    {
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate)
                        {
                            llmResponseAccumulator.Append(updatePart.Text);
                        }
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();

                    // Add LLM's raw response to history (could be tool call or final answer)
                    chatHistory.Add(new AssistantChatMessage(llmFullResponse));

                    if (McpToolHelper.TryParseToolCall(llmFullResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        _logger.LogInformation($"LLM requested tool: {parsedToolName} with arguments: {JsonSerializer.Serialize(toolArguments)}");
                        
                        // ChatId is now handled by ChatContextProvider within the tool method itself if needed.
                        // No need to inject it here anymore.

                        try
                        {
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);

                            string toolResultString;
                            if (toolResultObject == null) {
                                toolResultString = "Tool executed successfully with no return value.";
                            } else if (toolResultObject is string s) {
                                toolResultString = s;
                            } else {
                                toolResultString = JsonSerializer.Serialize(toolResultObject, new JsonSerializerOptions { WriteIndented = false });
                            }

                            _logger.LogInformation($"Tool {parsedToolName} executed. Result: {toolResultString}");
                            // Revert to using SystemChatMessage for tool feedback
                            string toolFeedback = $"[Tool Output for '{parsedToolName}']: {toolResultString}";
                            chatHistory.Add(new UserChatMessage(toolFeedback));
                            _logger.LogInformation($"Added SystemChatMessage to history for LLM: {toolFeedback}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error executing tool {parsedToolName}.");
                            string errorMessage = $"Error executing tool {parsedToolName}: {ex.Message}.";
                            // Revert to using SystemChatMessage for tool error feedback
                            string errorFeedback = $"[Tool Error for '{parsedToolName}']: {errorMessage}";
                            chatHistory.Add(new SystemChatMessage(errorFeedback));
                            _logger.LogInformation($"Added SystemChatMessage to history for LLM: {errorFeedback}");
                        }
                    }
                    else
                    {
                        yield return llmFullResponse;
                        yield break; 
                    }
                }

                _logger.LogWarning("Max tool call cycles reached for chat {ChatId}.", ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                ChatContextProvider.Clear(); // Clear ChatId from async context
            }
        }
    }
}
