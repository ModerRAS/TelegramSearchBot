using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class AgentToolRegistryService {
        private static readonly TimeSpan StartupWaitTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(250);
        private readonly IConnectionMultiplexer _redis;
        private readonly ToolExecutor _toolExecutor;
        private readonly ILogger<AgentToolRegistryService> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private string _lastDefinitionsJson = string.Empty;

        public AgentToolRegistryService(
            IConnectionMultiplexer redis,
            ToolExecutor toolExecutor,
            ILogger<AgentToolRegistryService> logger) {
            _redis = redis;
            _toolExecutor = toolExecutor;
            _logger = logger;
        }

        public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default) {
            try {
                var json = await _redis.GetDatabase().StringGetAsync(LlmAgentRedisKeys.AgentToolDefs);
                if (!json.HasValue || string.IsNullOrWhiteSpace(json.ToString())) {
                    _logger.LogWarning("No tool definitions found in Redis. Agent will have limited tools.");
                    return false;
                }

                var toolDefsJson = json.ToString();
                if (string.Equals(toolDefsJson, _lastDefinitionsJson, StringComparison.Ordinal)) {
                    return true;
                }

                await _refreshLock.WaitAsync(cancellationToken);
                try {
                    if (string.Equals(toolDefsJson, _lastDefinitionsJson, StringComparison.Ordinal)) {
                        return true;
                    }

                    var toolDefs = JsonConvert.DeserializeObject<List<ProxyToolDefinition>>(toolDefsJson);
                    if (toolDefs == null || toolDefs.Count == 0) {
                        _logger.LogWarning("Empty tool definitions from Redis.");
                        return false;
                    }

                    McpToolHelper.RegisterProxyTools(toolDefs, ExecuteProxyToolAsync);
                    _lastDefinitionsJson = toolDefsJson;

                    _logger.LogInformation(
                        "Imported {Count} tool definitions from Redis. ToolNames={ToolNames}",
                        toolDefs.Count,
                        string.Join(",", toolDefs.Select(t => t.Name).Take(80)));
                    return true;
                } finally {
                    _refreshLock.Release();
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _logger.LogWarning(
                    ex,
                    "Failed to import tool definitions from Redis. Agent will keep its current tools. ErrorSummary={ErrorSummary}",
                    ex.GetLogSummary());
                return false;
            }
        }

        public async Task RefreshUntilAvailableAsync(CancellationToken cancellationToken = default) {
            var deadline = DateTime.UtcNow + StartupWaitTimeout;
            while (!cancellationToken.IsCancellationRequested) {
                if (await RefreshAsync(cancellationToken)) {
                    return;
                }

                if (DateTime.UtcNow >= deadline) {
                    _logger.LogWarning("Timed out waiting for agent tool definitions in Redis. Agent will start with limited tools.");
                    return;
                }

                await Task.Delay(StartupPollInterval, cancellationToken);
            }
        }

        private async Task<string> ExecuteProxyToolAsync(string toolName, Dictionary<string, string> arguments) {
            long remoteChatId = 0, remoteUserId = 0, remoteMessageId = 0;
            if (arguments.TryGetValue("__chatId", out var cid)) {
                long.TryParse(cid, out remoteChatId);
                arguments.Remove("__chatId");
            }
            if (arguments.TryGetValue("__userId", out var uid)) {
                long.TryParse(uid, out remoteUserId);
                arguments.Remove("__userId");
            }
            if (arguments.TryGetValue("__messageId", out var mid)) {
                long.TryParse(mid, out remoteMessageId);
                arguments.Remove("__messageId");
            }

            _logger.LogInformation(
                "Proxy tool call routed to main process. Tool={ToolName}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, ArgumentKeys={ArgumentKeys}",
                toolName,
                remoteChatId,
                remoteUserId,
                remoteMessageId,
                string.Join(",", arguments.Keys));

            try {
                return await _toolExecutor.ExecuteRemoteToolAsync(
                    toolName, arguments, remoteChatId, remoteUserId, remoteMessageId, CancellationToken.None);
            } catch (Exception ex) {
                _logger.LogError(
                    ex,
                    "Proxy tool call failed while routed to main process. Tool={ToolName}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, ErrorSummary={ErrorSummary}",
                    toolName,
                    remoteChatId,
                    remoteUserId,
                    remoteMessageId,
                    ex.GetLogSummary());
                throw;
            }
        }
    }
}
