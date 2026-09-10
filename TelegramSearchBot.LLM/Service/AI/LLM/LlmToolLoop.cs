using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>Identity + resume data captured into iteration-limit continuation snapshots.</summary>
    public sealed class LlmToolLoopMeta {
        public long ChatId { get; set; }
        public long OriginalMessageId { get; set; }
        public long UserId { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public int ChannelId { get; set; }
        /// <summary>Base cycle count when resuming (added on top of this run's cycles).</summary>
        public int BaseCycles { get; set; }
        /// <summary>Pre-existing accumulated content when resuming (included in snapshots; only
        /// deltas beyond it are yielded).</summary>
        public string InitialContent { get; set; } = string.Empty;
    }

    /// <summary>Everything one agent run needs.</summary>
    public sealed class LlmAgentRunRequest {
        public ILlmTransport Transport { get; set; } = null!;
        public string SystemPrompt { get; set; } = string.Empty;
        /// <summary>Normalized chat history INCLUDING the input user message (no system entry).</summary>
        public List<LlmMessage> History { get; set; } = [];
        public IReadOnlyList<LlmToolSpec>? Tools { get; set; }
        public LlmTransportConfig Config { get; set; } = new();
        public ToolContext ToolContext { get; set; } = new();
        public LlmToolLoopMeta Meta { get; set; } = new();
        public LlmExecutionContext? ExecutionContext { get; set; }
    }

    /// <summary>
    /// The single shared agent tool-call loop. Owns the normalized <see cref="LlmMessage"/>
    /// history, drives the transport turn by turn, executes tools via <see cref="McpToolHelper"/>,
    /// yields accumulated-content snapshots, and builds v2 continuation snapshots.
    /// </summary>
    public static class LlmToolLoop {
        private const string MalformedToolCallCorrection =
            "Tool call failed before execution due to malformed tool metadata. Please verify the tool name and parameters, then try again.";

        public static async IAsyncEnumerable<string> RunAsync(
            LlmAgentRunRequest run,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var maxToolCycles = Env.MaxToolCycles;
            var contentBuilder = new StringBuilder(run.Meta.InitialContent ?? string.Empty);
            var baseline = contentBuilder.Length;

            string NewContent() => contentBuilder.ToString(baseline, contentBuilder.Length - baseline);
            bool IsNative() => run.Transport.SupportsNativeTools && run.Tools != null && run.Tools.Count > 0;

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                LlmTurnResult? turn = null;
                var request = new LlmTurnRequest {
                    SystemPrompt = run.SystemPrompt,
                    History = run.History,
                    Tools = IsNative() ? run.Tools : null,
                    Config = run.Config
                };

                await foreach (var evt in run.Transport.StreamTurnAsync(request, cancellationToken).WithCancellation(cancellationToken)) {
                    switch (evt) {
                        case LlmStreamEvent.TextDelta textDelta:
                            contentBuilder.Append(textDelta.Delta);
                            if (contentBuilder.Length - baseline > 10) {
                                yield return NewContent();
                            }
                            break;
                        case LlmStreamEvent.ThinkingDelta:
                            // Reasoning is captured inside the turn result; not streamed to Telegram.
                            break;
                        case LlmStreamEvent.TurnCompleted completed:
                            turn = completed.Turn;
                            break;
                    }
                }

                turn ??= new LlmTurnResult();
                var turnText = (turn.Text ?? string.Empty).Trim();

                if (IsNative()) {
                    if (turn.ToolCalls.Count > 0) {
                        run.History.Add(LlmMessage.Assistant(turnText, turn.Reasoning, turn.ToolCalls));
                        var indicators = new StringBuilder();
                        foreach (var call in turn.ToolCalls) {
                            var argsDict = call.ParseArguments();
                            var (resultString, isError) = await ExecuteToolAsync(call.Name, argsDict, run.ToolContext);
                            run.History.Add(LlmMessage.ToolResult(call.Id, call.Name, resultString, isError));
                            indicators.Append(McpToolHelper.FormatToolCallDisplay(call.Name, argsDict));
                        }
                        contentBuilder.Append(indicators.ToString());
                        yield return NewContent();
                        continue;
                    }

                    if (turn.MalformedToolCall) {
                        // Transport reported malformed metadata; ask the model to self-correct.
                        run.History.Add(LlmMessage.User(MalformedToolCallCorrection));
                        continue;
                    }

                    run.History.Add(LlmMessage.Assistant(turnText, turn.Reasoning));
                    yield break;
                }

                // Text-embedded (XML) tool protocol.
                if (!string.IsNullOrWhiteSpace(turnText)) {
                    run.History.Add(LlmMessage.Assistant(turnText, turn.Reasoning));
                }

                if (McpToolHelper.TryParseToolCalls(turnText, out var parsedToolCalls) && parsedToolCalls.Count > 0) {
                    var first = parsedToolCalls[0];
                    if (parsedToolCalls.Count > 1) {
                        Serilog.Log.Warning(
                            "LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.",
                            parsedToolCalls.Count, first.toolName);
                    }
                    var (resultString, isError) = await ExecuteToolAsync(first.toolName, first.arguments, run.ToolContext);
                    contentBuilder.Append(McpToolHelper.FormatToolCallDisplay(first.toolName, first.arguments));
                    yield return NewContent();
                    run.History.Add(LlmMessage.User(isError
                        ? $"[Tool '{first.toolName}' Execution Failed. Error: {resultString}]"
                        : $"[Executed Tool '{first.toolName}'. Result: {resultString}]"));
                    continue;
                }

                yield break;
            }

            // Iteration limit reached — persist v2 continuation snapshot for user confirmation.
            if (run.ExecutionContext != null) {
                run.ExecutionContext.IterationLimitReached = true;
                run.ExecutionContext.SnapshotData = new LlmContinuationSnapshot {
                    SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
                    ChatId = run.Meta.ChatId,
                    OriginalMessageId = run.Meta.OriginalMessageId,
                    UserId = run.Meta.UserId,
                    ModelName = run.Meta.ModelName,
                    Provider = run.Meta.Provider,
                    ChannelId = run.Meta.ChannelId,
                    LastAccumulatedContent = contentBuilder.ToString(),
                    CyclesSoFar = run.Meta.BaseCycles + maxToolCycles,
                    NormalizedHistory = new List<LlmMessage>(run.History)
                };
            }
        }

        private static async Task<(string Result, bool IsError)> ExecuteToolAsync(
            string toolName, Dictionary<string, string> argsDict, ToolContext toolContext) {
            try {
                object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, argsDict, toolContext);
                var result = McpToolHelper.ConvertToolResultToString(toolResultObject);
                return (result, false);
            } catch (Exception ex) {
                Serilog.Log.Error(ex, "Error executing tool {ToolName}", toolName);
                return ($"Error executing tool {toolName}: {ex.GetLogSummary()}", true);
            }
        }
    }
}
