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
    /// Unit tests for the shared agent tool-call loop (LlmToolLoop): the loop owns the
    /// normalized LlmMessage history; a scripted ILlmTransport feeds turns.
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

        /// <summary>Scripted transport: records per-turn requests, replays scripted turn factories.</summary>
        private sealed class ScriptedTransport : ILlmTransport {
            private readonly Queue<Func<LlmTurnRequest, IEnumerable<LlmStreamEvent>>> _turns = new();

            public bool SupportsNativeTools { get; set; }
            public List<LlmTurnRequest> Requests { get; } = new();

            public void EnqueueTurn(Func<LlmTurnRequest, IEnumerable<LlmStreamEvent>> turnFactory) => _turns.Enqueue(turnFactory);

            public async IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(
                LlmTurnRequest request,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
                Requests.Add(request);
                foreach (var evt in _turns.Dequeue()(request)) {
                    yield return evt;
                }
                await Task.CompletedTask;
            }
        }

        private static Func<LlmTurnRequest, IEnumerable<LlmStreamEvent>> Turn(
            string text, List<LlmToolCall>? toolCalls = null) {
            return _ => new List<LlmStreamEvent> {
                new LlmStreamEvent.TextDelta(text),
                new LlmStreamEvent.TurnCompleted(new LlmTurnResult {
                    Text = text,
                    ToolCalls = toolCalls ?? new List<LlmToolCall>(),
                    StreamedAny = !string.IsNullOrEmpty(text)
                })
            };
        }

        [Fact]
        public async Task TextProtocol_ParsesToolCall_AppendsAssistantAndFeedback() {
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
                async (server, tool, args) => { await Task.CompletedTask; return "mock:v"; });
            try {
                const string xml = "<tool name=\"mcp_test-server_loopTool\"><input>v</input></tool>";
                var transport = new ScriptedTransport();
                transport.EnqueueTurn(Turn(xml));
                transport.EnqueueTurn(Turn("final answer text"));

                var history = new List<LlmMessage> { LlmMessage.User("hi") };
                var yielded = new List<string>();
                var run = new LlmAgentRunRequest {
                    Transport = transport,
                    SystemPrompt = "sys",
                    History = history,
                    Tools = null,
                    ToolContext = ToolCtx(),
                    Meta = Meta()
                };
                await foreach (var item in LlmToolLoop.RunAsync(run, CancellationToken.None)) {
                    yielded.Add(item);
                }

                Assert.Equal(2, transport.Requests.Count);
                Assert.Contains("final answer text", yielded[^1]);
                // History: user input, assistant tool call turn, tool feedback user message, final assistant.
                Assert.Equal(4, history.Count);
                Assert.Equal(LlmRole.User, history[0].Role);
                Assert.Equal(LlmRole.Assistant, history[1].Role);
                Assert.Contains(xml, history[1].Text);
                Assert.Equal(LlmRole.User, history[2].Role);
                Assert.Contains("[Executed Tool 'mcp_test-server_loopTool'. Result: mock:v]", history[2].Text);
                Assert.Equal(LlmRole.Assistant, history[3].Role);
            } finally {
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task NativeProtocol_LoopOwnsHistory_AppendsToolResults() {
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
                var transport = new ScriptedTransport { SupportsNativeTools = true };
                transport.EnqueueTurn(Turn("calling tool", new List<LlmToolCall> {
                    new() { Id = "call-1", Name = "mcp_test-server_nativeTool", ArgumentsJson = "{\"input\":\"v\"}" }
                }));
                transport.EnqueueTurn(Turn("all done now"));

                var history = new List<LlmMessage> { LlmMessage.User("hi") };
                var yielded = new List<string>();
                var run = new LlmAgentRunRequest {
                    Transport = transport,
                    SystemPrompt = "sys",
                    History = history,
                    Tools = McpToolHelper.GetLlmToolSpecs(),
                    ToolContext = ToolCtx(),
                    Meta = Meta()
                };
                await foreach (var item in LlmToolLoop.RunAsync(run, CancellationToken.None)) {
                    yielded.Add(item);
                }

                Assert.Equal(2, transport.Requests.Count);
                // Turn-2 request history must contain the assistant tool call + tool result.
                var turn2 = transport.Requests[1];
                Assert.Equal(4, turn2.History.Count);
                Assert.Equal(LlmRole.Assistant, turn2.History[1].Role);
                Assert.NotNull(turn2.History[1].ToolCalls);
                Assert.Equal("call-1", turn2.History[1].ToolCalls![0].Id);
                Assert.Equal(LlmRole.Tool, turn2.History[2].Role);
                Assert.Equal("call-1", turn2.History[2].ToolCallId);
                Assert.Equal("native-ok", turn2.History[2].Text);
                Assert.Equal(4, history.Count);
                Assert.Contains("all done now", yielded[^1]);
            } finally {
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task IterationLimit_BuildsV2SnapshotWithNormalizedHistory() {
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
                var transport = new ScriptedTransport { SupportsNativeTools = true };
                for (var i = 0; i < 3; i++) {
                    var callId = $"call-{i}";
                    transport.EnqueueTurn(Turn("working", new List<LlmToolCall> {
                        new() { Id = callId, Name = "mcp_test-server_endlessTool", ArgumentsJson = "{}" }
                    }));
                }

                var executionContext = new LlmExecutionContext();
                var history = new List<LlmMessage> { LlmMessage.User("go") };
                var run = new LlmAgentRunRequest {
                    Transport = transport,
                    SystemPrompt = "sys",
                    History = history,
                    Tools = McpToolHelper.GetLlmToolSpecs(),
                    ToolContext = ToolCtx(),
                    Meta = Meta(),
                    ExecutionContext = executionContext
                };
                await foreach (var item in LlmToolLoop.RunAsync(run, CancellationToken.None)) {
                }

                Assert.True(executionContext.IterationLimitReached);
                var snapshot = executionContext.SnapshotData;
                Assert.NotNull(snapshot);
                Assert.Equal(2, snapshot.CyclesSoFar);
                Assert.Equal("Test", snapshot.Provider);
                Assert.Equal(9, snapshot.ChannelId);
                Assert.NotNull(snapshot.NormalizedHistory);
                Assert.Equal(history.Count, snapshot.NormalizedHistory!.Count);
                Assert.Contains(snapshot.NormalizedHistory, m => m.Role == LlmRole.Tool && m.ToolCallId == "call-0");
                Assert.Null(snapshot.ProviderHistory);
            } finally {
                Env.MaxToolCycles = oldCycles;
                McpToolHelper.RegisterExternalTools(
                    new List<(string, McpToolHelper.ExternalToolInfo)>(),
                    async (server, tool, args) => { await Task.CompletedTask; return string.Empty; });
            }
        }

        [Fact]
        public async Task ResumeMode_YieldsOnlyNewContentButSnapshotsFullContent() {
            var transport = new ScriptedTransport();
            transport.EnqueueTurn(_ => new List<LlmStreamEvent> { new LlmStreamEvent.TextDelta(" continued text") });
            transport.EnqueueTurn(Turn("continued text"));

            var meta = Meta();
            meta.BaseCycles = 3;
            meta.InitialContent = "old content";
            var executionContext = new LlmExecutionContext();
            var yielded = new List<string>();
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = "sys",
                History = new List<LlmMessage> { LlmMessage.User("prior") },
                ToolContext = ToolCtx(),
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, CancellationToken.None)) {
                yielded.Add(item);
            }

            Assert.All(yielded, y => Assert.DoesNotContain("old content", y));
            Assert.Contains("continued text", yielded[0]);
        }
    }
}
