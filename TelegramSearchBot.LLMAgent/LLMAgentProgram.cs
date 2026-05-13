using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.LLMAgent {
    public static class LLMAgentProgram {
        public static async Task RunAsync(string[] args) {
            var effectiveArgs = NormalizeArgs(args);
            if (effectiveArgs.Length != 2 ||
                !long.TryParse(effectiveArgs[0], out var chatId) ||
                !int.TryParse(effectiveArgs[1], out var port)) {
                Console.Error.WriteLine("Usage: LLMAgent <chatId> <port>");
                Environment.ExitCode = 1;
                return;
            }

            using var services = BuildServices(port);
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LLMAgent");
            McpToolHelper.EnsureInitialized(
                typeof(Service.AgentToolService).Assembly,
                services, logger);

            // Import tool definitions from Redis and register as proxy tools
            await RegisterProxyToolsFromRedisAsync(services, logger);

            var loop = services.GetRequiredService<Service.AgentLoopService>();
            using var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownCts.Cancel();

            await loop.RunAsync(chatId, port, shutdownCts.Token);
        }

        private static async Task RegisterProxyToolsFromRedisAsync(ServiceProvider services, ILogger logger) {
            try {
                var redis = services.GetRequiredService<IConnectionMultiplexer>();
                logger.LogDebug("Loading proxy tool definitions from Redis key {RedisKey}.", LlmAgentRedisKeys.AgentToolDefs);
                var json = await redis.GetDatabase().StringGetAsync(LlmAgentRedisKeys.AgentToolDefs);
                if (!json.HasValue || string.IsNullOrWhiteSpace(json.ToString())) {
                    logger.LogWarning("No tool definitions found in Redis. Agent will have limited tools.");
                    return;
                }

                var toolDefsJson = json.ToString();
                logger.LogDebug("Loaded proxy tool definition payload from Redis. Bytes={PayloadBytes}", System.Text.Encoding.UTF8.GetByteCount(toolDefsJson));
                var toolDefs = JsonConvert.DeserializeObject<List<ProxyToolDefinition>>(toolDefsJson);
                if (toolDefs == null || toolDefs.Count == 0) {
                    logger.LogWarning("Empty tool definitions from Redis.");
                    return;
                }

                var toolExecutor = services.GetRequiredService<Service.ToolExecutor>();

                // Register proxy tools with an executor that routes to the main process via Redis IPC
                McpToolHelper.RegisterProxyTools(toolDefs, async (toolName, arguments) => {
                    // Resolve chatId/userId/messageId from ToolContext if needed
                    // These will be set by the calling code in McpToolHelper before invoking
                    long remoteChatId = 0, remoteUserId = 0, remoteMessageId = 0;
                    if (arguments.TryGetValue("__chatId", out var cid)) { long.TryParse(cid, out remoteChatId); arguments.Remove("__chatId"); }
                    if (arguments.TryGetValue("__userId", out var uid)) { long.TryParse(uid, out remoteUserId); arguments.Remove("__userId"); }
                    if (arguments.TryGetValue("__messageId", out var mid)) { long.TryParse(mid, out remoteMessageId); arguments.Remove("__messageId"); }

                    logger.LogInformation(
                        "Proxy tool call routed to main process. Tool={ToolName}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, ArgumentKeys={ArgumentKeys}",
                        toolName,
                        remoteChatId,
                        remoteUserId,
                        remoteMessageId,
                        string.Join(",", arguments.Keys));
                    try {
                        return await toolExecutor.ExecuteRemoteToolAsync(
                            toolName, arguments, remoteChatId, remoteUserId, remoteMessageId, CancellationToken.None);
                    } catch (Exception ex) {
                        logger.LogError(
                            ex,
                            "Proxy tool call failed while routed to main process. Tool={ToolName}, ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}, ErrorSummary={ErrorSummary}",
                            toolName,
                            remoteChatId,
                            remoteUserId,
                            remoteMessageId,
                            ex.GetLogSummary());
                        throw;
                    }
                });

                logger.LogInformation(
                    "Imported {Count} tool definitions from Redis. ToolNames={ToolNames}",
                    toolDefs.Count,
                    string.Join(",", toolDefs.Select(t => t.Name).Take(80)));
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to import tool definitions from Redis. Agent will have limited tools. ErrorSummary={ErrorSummary}", ex.GetLogSummary());
            }
        }

        private static string[] NormalizeArgs(string[] args) {
            if (args.Length > 0 && args[0].Equals("LLMAgent", StringComparison.OrdinalIgnoreCase)) {
                return args.Skip(1).ToArray();
            }

            return args;
        }

        private static ServiceProvider BuildServices(int port) {
            var services = new ServiceCollection();
            services.AddLogging(builder => {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddSimpleConsole(options => {
                    options.SingleLine = true;
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });
            });
            services.AddHttpClient();
            services.AddHttpClient("OllamaClient");
            services.AddDbContext<DataDbContext>(options => {
                options.UseInMemoryDatabase($"llm-agent-{port}");
            }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect($"localhost:{port},abortConnect=false,connectTimeout=5000,connectRetry=5"));
            services.AddScoped<IMessageExtensionService, Service.InMemoryMessageExtensionService>();
            services.AddSingleton<IBotIdentityProvider, BotIdentityProvider>();
            services.AddScoped<IGroupLlmSettingsService, GroupLlmSettingsService>();
            services.AddScoped<OpenAIService>();
            services.AddScoped<OpenAIResponsesService>();
            services.AddScoped<OllamaService>();
            services.AddScoped<GeminiService>();
            services.AddScoped<AnthropicService>();
            services.AddScoped<Service.ToolExecutor>();
            services.AddScoped<Service.AgentToolService>();
            services.AddScoped<IFileToolService, FileToolService>();
            services.AddScoped<IBashToolService, BashToolService>();
            services.AddScoped<Service.IAgentTaskExecutor, Service.LlmServiceProxy>();
            services.AddScoped<Service.LlmServiceProxy>();
            services.AddSingleton<Service.GarnetClient>();
            services.AddSingleton<Service.GarnetRpcClient>();
            services.AddSingleton<Service.AgentLoopService>();
            return services.BuildServiceProvider();
        }
    }
}
