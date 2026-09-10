using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    /// <summary>
    /// Unit tests for the shared agent tool-call loop (LlmToolLoop). Uses a scripted
    /// ILlmTurnSource plus a mock external tool registered in McpToolHelper.
    /// </summary>
    public class LlmToolLoopTests {
        private static ToolContext ToolCtx() => new ToolContext { ChatId = 1, UserId = 2, MessageId = 3 };

        private static LlmToolLoopMeta Meta() => new LlmToolLoopMeta {
            ChatId = 1,
            OriginalMessageId = 3,
            UserId = 2,
            ModelName = "test-model",
            Provider = "Test",
            ChannelId = 9
        };

        /// <summary>Scripted source: each enqueued factory produces one turn's events.</summary>
        private sealed class ScriptedTurnSource : ILlmTurnSource {
            private readonly Queue<Func<string?, IEnumerable<LlmStreamEvent>>> _turns = new();

            public bool SupportsNativeTools { get; set; }
            public List<string?> ReceivedUserContents { get; } = new();
            public List<List<LlmToolResult>> CommittedResults { get; } = new();
            public List<SerializedChatMessage> TrackedHistory { get; } = new();

            public void EnqueueTurn(Func<string?, IEnumerable<LlmStreamEvent>> turnFactory) => _turns.Enqueue(turnFactory);

            public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
                string? userContent,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
                ReceivedUserContents.Add(userContent);
                var turn = _turns.Dequeue();
                foreach (var evt in turn(userContent)) {
                    yield return evt;
                }
                await Task.CompletedTask;
            }

            public void CommitAssistantTurn(string text, string reasoning, bool streamedAny) {
                TrackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = text });
            }

            public void CommitToolResults(IReadOnlyList<LlmToolResult> results) {
                CommittedResults.Add(results.ToList());
                TrackedHistory.Add(new SerializedChatMessage { Role = "user", Content = "tool-results" });
            }

            public IReadOnlyList<SerializedChatMessage> GetTrackedHistory() => TrackedHistory;
        }

        private static Func<string, string, Dictionary<string, string>, Task<string>> EchoExecutor() =>
            async (server, tool, args) => {
                await Task.CompletedTask;
                return $"mock:{args.GetValueOrDefault("input", "")}";
            };

        [Fact]
        public async Task TextProtocol_ParsesToolCall_Executes_SendsFeedback() {
            McpToolHelper.RegisterExternalTools(
                new List<(string, McpToolHelper.ExternalToolInfo)> {
                    ("test-server", new McpToolHelper.ExternalToolInfo {
                        ServerName = "test-server",
                        ToolName = "loopTool",
                        Description = "test tool",
                        Parameters = new List<McpToolHelper.ExternalToolParameter> {
                            new() { Name = "input", Type = "string", Description = "input", Required = true }
                        }
                    })
                },
                EchoExecutor());

            try {
                var source = new ScriptedTurnSource();
                const string xml = "<tool name=\"mcp_test-server_loopTool\"><input>v</input></tool>";
                source.EnqueueTurn(_ => new[] {
                    new LlmStreamEvent.TextDelta(xml),
                    ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult { Text = xml })
                });
                source.EnqueueTurn(_ => new[] {
                    new LlmStreamEvent.TextDelta("final answer text"),
                    ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult { Text = "final answer text" })
                });

                var yielded = new List<string>();
                await foreach (var item in LlmToolLoop.RunAsync(source, "hi", ToolCtx(), Meta(), null, CancellationToken.None)) {
                    yielded.Add(item);
                }

                Assert.Equal(2, source.ReceivedUserContents.Count);
                Assert.Equal("hi", source.ReceivedUserContents[0]);
                Assert.Contains("[Executed Tool 'mcp_test-server_loopTool'. Result: mock:v]", source.ReceivedUserContents[1]);
                // Cumulative snapshot semantics: one yield per text delta beyond 10 chars + one per tool display.
                Assert.True(yielded.Count >= 2);
                Assert.Contains("final answer text", yielded[^1]);
                Assert.Empty(source.CommittedResults);
            } finally {
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task NativeProtocol_CommitsToolResultsAndPassesNullUserContent() {
            McpToolHelper.RegisterExternalTools(
                new List<(string, McpToolHelper.ExternalToolInfo)> {
                    ("test-server", new McpToolHelper.ExternalToolInfo {
                        ServerName = "test-server",
                        ToolName = "nativeTool",
                        Description = "native test tool",
                        Parameters = new List<McpToolHelper.ExternalToolParameter>()
                    })
                },
                async (server, tool, args) => { await Task.CompletedTask; return "native-ok"; });

            try {
                var source = new ScriptedTurnSource { SupportsNativeTools = true };
                source.EnqueueTurn(_ => new[] {
                    ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                        Text = "calling tool",
                        ToolCalls = new List<LlmNativeToolCall> {
                            new() { Id = "call-1", Name = "mcp_test-server_nativeTool", ArgumentsJson = "{\"input\":\"v\"}" }
                        }
                    })
                });
                source.EnqueueTurn(_ => new[] {
                    ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult { Text = "all done now" })
                });

                var yielded = new List<string>();
                await foreach (var item in LlmToolLoop.RunAsync(source, null, ToolCtx(), Meta(), null, CancellationToken.None)) {
                    yielded.Add(item);
                }

                Assert.Equal(2, source.ReceivedUserContents.Count);
                Assert.Null(source.ReceivedUserContents[1]);
                Assert.Single(source.CommittedResults);
                Assert.Equal("native-ok", source.CommittedResults[0][0].Result);
                Assert.Equal("call-1", source.CommittedResults[0][0].ToolCallId);
            } finally {
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task IterationLimit_BuildsContinuationSnapshot() {
            McpToolHelper.RegisterExternalTools(
                new List<(string, McpToolHelper.ExternalToolInfo)> {
                    ("test-server", new McpToolHelper.ExternalToolInfo {
                        ServerName = "test-server",
                        ToolName = "endlessTool",
                        Description = "endless",
                        Parameters = new List<McpToolHelper.ExternalToolParameter>()
                    })
                },
                async (server, tool, args) => { await Task.CompletedTask; return "still running"; });

            var oldCycles = Env.MaxToolCycles;
            Env.MaxToolCycles = 2;
            try {
                var source = new ScriptedTurnSource { SupportsNativeTools = true };
                for (var i = 0; i < 3; i++) {
                    var callId = $"call-{i}";
                    source.EnqueueTurn(_ => new[] {
                        ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                            Text = "working",
                            ToolCalls = new List<LlmNativeToolCall> {
                                new() { Id = callId, Name = "mcp_test-server_endlessTool", ArgumentsJson = "{}" }
                            }
                        })
                    });
                }

                var executionContext = new LlmExecutionContext();
                await foreach (var item in LlmToolLoop.RunAsync(source, null, ToolCtx(), Meta(), executionContext, CancellationToken.None)) {
                }

                Assert.True(executionContext.IterationLimitReached);
                var snapshot = executionContext.SnapshotData;
                Assert.NotNull(snapshot);
                Assert.Equal(2, snapshot.CyclesSoFar);
                Assert.Equal("Test", snapshot.Provider);
                Assert.Equal(9, snapshot.ChannelId);
                Assert.Equal(2, source.CommittedResults.Count);
            } finally {
                Env.MaxToolCycles = oldCycles;
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task ResumeMode_YieldsOnlyNewContentButSnapshotsFullContent() {
            var source = new ScriptedTurnSource();
            source.EnqueueTurn(_ => new[] { new LlmStreamEvent.TextDelta(" continued text") });
            source.EnqueueTurn(_ => new[] {
                ( LlmStreamEvent ) new LlmStreamEvent.TurnCompleted(new LlmTurnResult { Text = "continued text" })
            });

            var meta = Meta();
            meta.BaseCycles = 3;
            meta.InitialContent = "old content";
            var executionContext = new LlmExecutionContext();
            var yielded = new List<string>();
            await foreach (var item in LlmToolLoop.RunAsync(source, null, ToolCtx(), meta, executionContext, CancellationToken.None)) {
                yielded.Add(item);
            }

            Assert.All(yielded, y => Assert.DoesNotContain("old content", y));
            Assert.Contains("continued text", yielded[0]);
        }
    }
}
