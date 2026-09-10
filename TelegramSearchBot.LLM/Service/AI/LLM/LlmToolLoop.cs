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
    /// <summary>Identity data captured into iteration-limit continuation snapshots.</summary>
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

    /// <summary>
    /// The single shared agent tool-call loop. Drives <see cref="ILlmTurnSource"/> turns,
    /// executes tools via <see cref="McpToolHelper"/>, yields accumulated-content snapshots,
    /// and builds the iteration-limit continuation snapshot. Replaces the per-provider
    /// copies of this cycle.
    /// </summary>
    public static class LlmToolLoop {
        public static async IAsyncEnumerable<string> RunAsync(
            ILlmTurnSource source,
            string? initialUserContent,
            ToolContext toolContext,
            LlmToolLoopMeta meta,
            LlmExecutionContext? executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var maxToolCycles = Env.MaxToolCycles;
            var contentBuilder = new StringBuilder(meta.InitialContent ?? string.Empty);
            var baseline = contentBuilder.Length;

            string NewContent() => contentBuilder.ToString(baseline, contentBuilder.Length - baseline);

            var userContent = initialUserContent;

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                LlmTurnResult? turn = null;

                await foreach (var evt in source.StreamTurnAsync(userContent, cancellationToken).WithCancellation(cancellationToken)) {
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

                if (source.SupportsNativeTools) {
                    if (turn.ToolCalls.Count > 0) {
                        var results = new List<LlmToolResult>();
                        var indicators = new StringBuilder();
                        foreach (var call in turn.ToolCalls) {
                            var argsDict = call.ParseArguments();
                            var (resultString, isError) = await ExecuteToolAsync(call.Name, argsDict, toolContext);
                            results.Add(new LlmToolResult {
                                ToolCallId = call.Id,
                                Name = call.Name,
                                ArgumentsJson = call.ArgumentsJson,
                                Result = resultString,
                                IsError = isError
                            });
                            indicators.Append(McpToolHelper.FormatToolCallDisplay(call.Name, argsDict));
                        }
                        contentBuilder.Append(indicators.ToString());
                        yield return NewContent();
                        source.CommitToolResults(results);
                        userContent = null;
                        continue;
                    }

                    if (turn.MalformedToolCall) {
                        // The source appended a self-correction message to its own history.
                        userContent = null;
                        continue;
                    }

                    source.CommitAssistantTurn(turnText, turn.Reasoning, turn.StreamedAny);
                    yield break;
                }

                // Text-embedded tool protocol.
                source.CommitAssistantTurn(turnText, turn.Reasoning, turn.StreamedAny);

                if (McpToolHelper.TryParseToolCalls(turnText, out var parsedToolCalls) && parsedToolCalls.Count > 0) {
                    var first = parsedToolCalls[0];
                    if (parsedToolCalls.Count > 1) {
                        LogMultipleToolCalls(first.toolName, parsedToolCalls.Count);
                    }
                    var (resultString, isError) = await ExecuteToolAsync(first.toolName, first.arguments, toolContext);
                    contentBuilder.Append(McpToolHelper.FormatToolCallDisplay(first.toolName, first.arguments));
                    yield return NewContent();
                    userContent = isError
                        ? $"[Tool '{first.toolName}' Execution Failed. Error: {resultString}]"
                        : $"[Executed Tool '{first.toolName}'. Result: {resultString}]";
                    continue;
                }

                yield break;
            }

            // Iteration limit reached — persist continuation snapshot for user confirmation.
            if (executionContext != null) {
                executionContext.IterationLimitReached = true;
                executionContext.SnapshotData = new LlmContinuationSnapshot {
                    SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
                    ChatId = meta.ChatId,
                    OriginalMessageId = meta.OriginalMessageId,
                    UserId = meta.UserId,
                    ModelName = meta.ModelName,
                    Provider = meta.Provider,
                    ChannelId = meta.ChannelId,
                    LastAccumulatedContent = contentBuilder.ToString(),
                    CyclesSoFar = meta.BaseCycles + maxToolCycles,
                    ProviderHistory = new List<SerializedChatMessage>(source.GetTrackedHistory() ?? []),
                };
            }
        }

        private static void LogMultipleToolCalls(string toolName, int count) {
            Serilog.Log.Warning(
                "LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.",
                count, toolName);
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
