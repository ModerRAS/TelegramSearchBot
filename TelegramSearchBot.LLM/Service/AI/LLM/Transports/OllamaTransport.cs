using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    public sealed class OllamaTransport : ILlmTransport {
        /// <summary>
        /// Builds the transport from a channel/binding pair; pulls the model locally if missing.
        /// </summary>
        public static async Task<LlmTransportBundle> CreateAsync(LLMChannel channel, LLMApiBinding binding, string modelName,
            string systemPrompt, ILogger logger, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            // ponytail: "OllamaClient" named client may be unregistered in some hosts; fall back to a fresh client.
            HttpClient httpClient = httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
            httpClient.BaseAddress = new Uri(endpoint);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName, logger)) {
                throw new InvalidOperationException($"Ollama model {modelName} is not available locally and could not be pulled.");
            }
            ollama.SelectedModel = modelName;

            var transport = new OllamaTransport(logger, ollama, systemPrompt);
            var config = new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = endpoint,
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel
            };
            return new LlmTransportBundle(transport, config);
        }

        /// <summary>Ensures the model exists locally, pulling it otherwise. Moved from OllamaModelApi.</summary>
        public static async Task<bool> CheckAndPullModelAsync(OllamaApiClient ollama, string modelName, ILogger logger) {
            logger.LogInformation("Checking for Ollama model: {ModelName}", modelName);
            try {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                    logger.LogInformation("Model {ModelName} found locally.", modelName);
                    return true;
                }

                logger.LogInformation("Model {ModelName} not found locally. Pulling...", modelName);

                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None)) {
                    if (status != null) {
                        logger.LogInformation("[{ModelName}] Pulling model {Percent}% - {Status}", modelName, status.Percent, status.Status);
                    }
                }
                logger.LogInformation("Model {ModelName} pull stream completed.", modelName);

                var modelsAfterPull = await ollama.ListLocalModelsAsync();
                if (!modelsAfterPull.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                    logger.LogError("Model {ModelName} still not found after pull attempt.", modelName);
                    return false;
                }
                logger.LogInformation("Model {ModelName} confirmed present after pull.", modelName);
                return true;
            } catch (Exception ex) {
                logger.LogError(ex, "Error checking or pulling Ollama model {ModelName}", modelName);
                return false;
            }
        }

        private readonly ILogger _logger;
        private readonly OllamaApiClient _ollama;
        private readonly string _systemPrompt;
        private OllamaSharp.Chat? _chat;

        public OllamaTransport(ILogger logger, OllamaApiClient ollama, string systemPrompt) {
            _logger = logger;
            _ollama = ollama;
            _systemPrompt = systemPrompt;
        }

        public bool SupportsNativeTools => false;

        public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
            LlmTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (_chat == null) {
                _chat = new OllamaSharp.Chat(_ollama, _systemPrompt);
                // Seed the session with all history except the newest user message.
                for (int i = 0; i < request.History.Count - 1; i++) {
                    var m = request.History[i];
                    if (m.Role == LlmRole.User) {
                        _chat.Messages.Add(new OllamaSharp.Models.Chat.Message { Role = ChatRole.User, Content = m.Text ?? string.Empty });
                    } else if (m.Role == LlmRole.Assistant) {
                        _chat.Messages.Add(new OllamaSharp.Models.Chat.Message { Role = ChatRole.Assistant, Content = m.Text ?? string.Empty });
                    }
                }
            }

            var lastUser = request.History.LastOrDefault(m => m.Role == LlmRole.User);
            var prompt = lastUser?.Text ?? string.Empty;

            var turnText = new StringBuilder();
            var streamedAny = false;
            await foreach (var token in _chat.SendAsync(prompt, cancellationToken).WithCancellation(cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                turnText.Append(token);
                streamedAny = true;
                yield return new LlmStreamEvent.TextDelta(token);
            }

            if (!streamedAny) {
                _logger.LogWarning("OllamaService: Ollama returned an empty stream during a tool cycle.");
            }

            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = turnText.ToString().Trim(),
                StreamedAny = streamedAny
            });
        }
    }}
