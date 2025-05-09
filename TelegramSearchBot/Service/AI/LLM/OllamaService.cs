using Microsoft.EntityFrameworkCore; 
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat; 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions; // For Regex
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json; // Using Newtonsoft
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Tools; // Added for DuckDuckGoSearchResult

namespace TelegramSearchBot.Service.AI.LLM
{
    // Standalone implementation, not using BaseLlmService
    public class OllamaService : IService, ILLMService 
    {
        public string ServiceName => "OllamaService";

        private readonly ILogger<OllamaService> _logger;
        private readonly DataDbContext _dbContext; 
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _availableToolsPromptPart;
        public string BotName { get; set; }

        // Constructor requires dependencies needed directly by this class
        public OllamaService(
            DataDbContext context, 
            ILogger<OllamaService> logger, 
            IServiceProvider serviceProvider, 
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context; 
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;

            // Initialize McpToolHelper (still needed)
            McpToolHelper.Initialize(_serviceProvider, _logger);

            // Register tools and generate the prompt part once (still needed)
            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
        }

        // --- Helper methods specific to this service ---

        public async Task<bool> CheckAndPullModelAsync(OllamaApiClient ollama, string modelName)
        {
            _logger.LogInformation("Checking for Ollama model: {ModelName}", modelName);
            try
            {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Model {ModelName} found locally.", modelName);
                    return true;
                }

                _logger.LogInformation("Model {ModelName} not found locally. Pulling...", modelName);
                
                // Consume the stream from PullModelAsync
                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None))
                {
                    if (status != null) {
                         // Adjust property names (Percent, Status) if they differ in your OllamaSharp version
                         _logger.LogInformation("[{ModelName}] Pulling model {Percent}% - {Status}", modelName, status.Percent, status.Status);
                    }
                }
                _logger.LogInformation("Model {ModelName} pull stream completed.", modelName);

                // Re-check if model exists after pull attempt completion
                var modelsAfterPull = await ollama.ListLocalModelsAsync();
                 if (!modelsAfterPull.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                     _logger.LogError("Model {ModelName} still not found after pull attempt.", modelName);
                     return false; // Indicate failure
                 }
                 _logger.LogInformation("Model {ModelName} confirmed present after pull.", modelName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking or pulling Ollama model {ModelName}", modelName);
                return false;
            }
        }

        // --- Main Execution Logic (Using OllamaSharp.Chat helper) ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            modelName = modelName ?? Env.OllamaModelName;
            if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} model name is not configured.";
                 yield break;
            }
             if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                 _logger.LogError("{ServiceName}: Channel or Gateway is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} channel/gateway is not configured.";
                 yield break;
            }

            // --- Client and Model Setup ---
            HttpClient httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient(); 
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                 yield return $"Error: Could not check or pull Ollama model '{modelName}'.";
                 yield break;
            }
            ollama.SelectedModel = modelName;

            // --- History and Prompt Setup ---
            // NOTE: History context is limited as OllamaSharp.Chat manages it.
            // Use helper method to format the prompt
            var systemPrompt = McpToolHelper.FormatSystemPrompt(BotName, ChatId, _availableToolsPromptPart); 

            var chat = new OllamaSharp.Chat(ollama, systemPrompt);

            ChatContextProvider.SetCurrentChatId(ChatId); 
            try
            {
                string nextMessageToSend = message.Content; 
                int maxToolCycles = 5;

                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var currentLlmResponseBuilder = new StringBuilder(); // Accumulates tokens for the current LLM response
                    bool receivedAnyToken = false;
                    
                    _logger.LogDebug("Sending to Ollama (Cycle {Cycle}): {Message}", cycle + 1, nextMessageToSend);
                    await foreach (var token in chat.SendAsync(nextMessageToSend, cancellationToken).WithCancellation(cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        currentLlmResponseBuilder.Append(token);
                        receivedAnyToken = true;
                        yield return currentLlmResponseBuilder.ToString(); // Yield current full message
                    }
                    string llmFullResponseText = currentLlmResponseBuilder.ToString().Trim();
                    _logger.LogDebug("LLM raw full response (Cycle {Cycle}): {Response}", cycle + 1, llmFullResponseText);

                    if (!receivedAnyToken && cycle < maxToolCycles -1 && !string.IsNullOrEmpty(nextMessageToSend)) {
                         _logger.LogWarning("{ServiceName}: Ollama returned empty stream during tool cycle {Cycle} for input '{Input}'.", ServiceName, cycle + 1, nextMessageToSend);
                    }
                    
                    // --- Tool Handling (using the full accumulated response text) ---
                    // No need for McpToolHelper.CleanLlmResponse before TryParseToolCall if tool calls are expected in raw response.
                    if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any())
                    {
                        var firstToolCall = parsedToolCalls[0];
                        string parsedToolName = firstToolCall.toolName;
                        Dictionary<string, string> toolArguments = firstToolCall.arguments;

                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        if (parsedToolCalls.Count > 1)
                        {
                            _logger.LogWarning("{ServiceName}: LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.", ServiceName, parsedToolCalls.Count, parsedToolName);
                        }
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject); 
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        nextMessageToSend = $"{feedbackPrefix}{toolResultString}]"; 
                        _logger.LogInformation("Prepared feedback for next LLM call: {Feedback}", nextMessageToSend);
                        // Continue loop - the next chat.SendAsync will send this feedback
                    }
                    else
                    {
                        // Not a tool call. The stream has already yielded the full content.
                        if (string.IsNullOrWhiteSpace(llmFullResponseText) && receivedAnyToken) {
                             _logger.LogWarning("{ServiceName}: LLM returned empty final non-tool response after trimming for ChatId {ChatId}.", ServiceName, ChatId);
                        } else if (!receivedAnyToken && string.IsNullOrEmpty(llmFullResponseText)) {
                             _logger.LogWarning("{ServiceName}: LLM returned empty stream and empty final non-tool response for ChatId {ChatId}.", ServiceName, ChatId);
                        }
                        yield break; 
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}.", ServiceName, ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                ChatContextProvider.Clear(); 
            }
        }

        // ConvertToolResultToString has been moved to McpToolHelper
    }
}
