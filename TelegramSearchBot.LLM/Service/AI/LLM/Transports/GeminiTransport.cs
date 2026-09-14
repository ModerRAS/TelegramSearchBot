using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using System.Net.Http;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    public sealed class GeminiTransport : ILlmTransport {
        /// <summary>Builds the transport from a channel/binding pair (Gemini uses the channel ApiKey directly).</summary>
        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName,
            bool supportsVision, ILogger logger, IHttpClientFactory httpClientFactory) {
            var googleAI = new GoogleAi(channel.ApiKey, client: httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            var transport = new GeminiTransport(model, supportsVision);
            var config = new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding),
                ApiKey = channel.ApiKey,
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel,
                SupportsVision = supportsVision
            };
            return new LlmTransportBundle(transport, config);
        }

        private readonly GenerativeModel _model;
        private readonly bool _supportsVision;
        private ChatSession? _chatSession;

        public GeminiTransport(GenerativeModel model, bool supportsVision) {
            
            _model = model;
            _supportsVision = supportsVision;
        }

        public bool SupportsNativeTools => false;

        public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
            LlmTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (_chatSession == null) {
                var seedHistory = ToContents(request.History.Take(request.History.Count - 1).ToList());
                _chatSession = _model.StartChat(history: seedHistory);
            }

            var lastUser = request.History.LastOrDefault(m => m.Role == LlmRole.User);
            var prompt = lastUser?.Text ?? string.Empty;

            var turnText = new StringBuilder();
            await foreach (var chunk in _chatSession.StreamContentAsync(prompt).WithCancellation(cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                turnText.Append(chunk.Text);
                yield return new LlmStreamEvent.TextDelta(chunk.Text);
            }

            yield return new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                Text = turnText.ToString().Trim(),
                StreamedAny = turnText.Length > 0
            });
        }

        private List<GenerativeAI.Types.Content> ToContents(List<LlmMessage> messages) {
            var contents = new List<GenerativeAI.Types.Content>();
            foreach (var m in messages) {
                if (m.Role == LlmRole.User) {
                    var role = Roles.User;
                    if (m.ImagePng != null && _supportsVision) {
                        var parts = new List<Part>();
                        if (!string.IsNullOrWhiteSpace(m.Text)) {
                            parts.Add(new Part { Text = m.Text.Trim() });
                        }
                        parts.Add(new Part {
                            InlineData = new GenerativeAI.Types.Blob {
                                MimeType = m.ImageMediaType ?? "image/png",
                                Data = Convert.ToBase64String(m.ImagePng)
                            }
                        });
                        contents.Add(new Content { Parts = parts, Role = role });
                    } else {
                        contents.Add(new Content(m.Text?.Trim() ?? string.Empty, role));
                    }
                } else if (m.Role == LlmRole.Assistant) {
                    contents.Add(new Content(m.Text?.Trim() ?? string.Empty, Roles.Model));
                }
            }
            return contents;
        }
    }}
