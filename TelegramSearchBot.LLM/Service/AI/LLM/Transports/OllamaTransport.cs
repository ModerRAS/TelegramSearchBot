using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
