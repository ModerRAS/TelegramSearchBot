using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using Telegram.Bot;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class CodingAgentReportConsumer : BackgroundService {
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CodingAgentReportConsumer> _logger;
        private readonly SemaphoreSlim _concurrencyLimiter = new(2, 2);

        public CodingAgentReportConsumer(
            IConnectionMultiplexer redis,
            IServiceProvider serviceProvider,
            ILogger<CodingAgentReportConsumer> logger) {
            _redis = redis;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            if (!Env.EnableCodingAgentTool) {
                _logger.LogDebug("Coding agent tool disabled; report consumer will not start.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var result = await _redis.GetDatabase().ExecuteAsync("BRPOP", LlmAgentRedisKeys.CodingAgentReportQueue, 2);
                    if (result.IsNull) {
                        continue;
                    }

                    var parts = ( RedisResult[] ) result!;
                    if (parts.Length != 2) {
                        continue;
                    }

                    var payload = parts[1].ToString();
                    if (string.IsNullOrWhiteSpace(payload)) {
                        continue;
                    }

                    CodingAgentJobReport report;
                    try {
                        report = JsonConvert.DeserializeObject<CodingAgentJobReport>(payload);
                    } catch (JsonException ex) {
                        _logger.LogWarning(ex, "Ignoring malformed coding agent report payload.");
                        continue;
                    }

                    if (report == null) {
                        continue;
                    }

                    await _concurrencyLimiter.WaitAsync(stoppingToken);
                    _ = Task.Run(async () => {
                        try {
                            await ProcessReportAsync(report, stoppingToken);
                        } catch (Exception ex) when (ex is not OperationCanceledException) {
                            _logger.LogError(ex, "Failed to process coding agent report. JobId={JobId}", report.JobId);
                        } finally {
                            _concurrencyLimiter.Release();
                        }
                    }, stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (RedisException ex) {
                    _logger.LogWarning(ex, "Redis error in CodingAgentReportConsumer, retrying in 1 s.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    _logger.LogError(ex, "Unexpected error in CodingAgentReportConsumer, retrying in 1 s.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }

        private async Task ProcessReportAsync(CodingAgentJobReport report, CancellationToken stoppingToken) {
            using var scope = _serviceProvider.CreateScope();
            var sendMessageService = scope.ServiceProvider.GetRequiredService<ISendMessageService>();

            await SendReportMessageAsync(sendMessageService, report);

            if (!Env.EnableOpenAI) {
                _logger.LogInformation("Skipping coding agent LLM continuation because EnableOpenAI is false. JobId={JobId}", report.JobId);
                return;
            }

            try {
                if (Env.EnableLLMAgentProcess) {
                    await ResumeWithAgentProcessAsync(scope.ServiceProvider, report, stoppingToken);
                } else {
                    await ResumeInProcessAsync(scope.ServiceProvider, report, stoppingToken);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogError(ex, "Coding agent auto continuation failed. JobId={JobId}", report.JobId);
                await SendTextAsync(
                    sendMessageService,
                    $"Coding agent report was received, but auto continuation failed:\n{ex.GetLogSummary()}",
                    report.ChatId,
                    ToTelegramMessageId(report.MessageId));
            }
        }

        private async Task ResumeWithAgentProcessAsync(IServiceProvider scopedProvider, CodingAgentJobReport report, CancellationToken stoppingToken) {
            var queueService = scopedProvider.GetRequiredService<LLMTaskQueueService>();
            var sendMessageService = scopedProvider.GetRequiredService<ISendMessageService>();
            var botIdentity = await EnsureBotIdentityAsync(scopedProvider, stoppingToken);
            var replyTo = ToTelegramMessageId(report.MessageId);
            LlmContinuationSnapshot continuationSnapshot = null;

            for (var attempt = 0; attempt <= Env.CodingAgentMaxAutoResumeContinuations; attempt++) {
                AgentTaskStreamHandle handle;
                if (attempt == 0) {
                    handle = await queueService.EnqueueSyntheticMessageTaskAsync(
                        report.ChatId,
                        report.UserId,
                        report.MessageId,
                        BuildContinuationPrompt(report),
                        botIdentity.UserName,
                        botIdentity.UserId,
                        report.CompletedAtUtc == DateTime.MinValue ? DateTime.UtcNow : report.CompletedAtUtc,
                        stoppingToken);
                } else if (continuationSnapshot != null) {
                    handle = await queueService.EnqueueContinuationTaskAsync(
                        continuationSnapshot,
                        botIdentity.UserName,
                        botIdentity.UserId,
                        stoppingToken);
                } else {
                    return;
                }

                await sendMessageService.SendDraftStream(
                    handle.ReadSnapshotsAsync(stoppingToken),
                    report.ChatId,
                    replyTo,
                    attempt == 0 ? "Coding agent report received; continuing..." : "Continuing coding-agent follow-up...",
                    stoppingToken);

                var terminalChunk = await handle.Completion.WaitAsync(stoppingToken);
                if (terminalChunk.Type == AgentChunkType.Error) {
                    await SendTextAsync(sendMessageService, $"AI Agent continuation failed: {terminalChunk.ErrorMessage}", report.ChatId, replyTo);
                    return;
                }

                if (terminalChunk.Type == AgentChunkType.IterationLimitReached && terminalChunk.ContinuationSnapshot != null) {
                    continuationSnapshot = terminalChunk.ContinuationSnapshot;
                    continue;
                }

                return;
            }

            await SendTextAsync(
                sendMessageService,
                $"Coding agent auto continuation stopped after {Env.CodingAgentMaxAutoResumeContinuations} continuation attempts.",
                report.ChatId,
                replyTo);
        }

        private async Task ResumeInProcessAsync(IServiceProvider scopedProvider, CodingAgentJobReport report, CancellationToken stoppingToken) {
            var generalLlmService = scopedProvider.GetRequiredService<IGeneralLLMService>();
            var sendMessageService = scopedProvider.GetRequiredService<ISendMessageService>();
            var replyTo = ToTelegramMessageId(report.MessageId);
            var executionContext = new LlmExecutionContext();
            var inputMessage = new Model.Data.Message {
                Content = BuildContinuationPrompt(report),
                DateTime = DateTime.UtcNow,
                FromUserId = report.UserId,
                GroupId = report.ChatId,
                MessageId = report.MessageId,
                ReplyToMessageId = report.MessageId,
                Id = -1
            };

            await sendMessageService.SendDraftStream(
                generalLlmService.ExecAsync(inputMessage, report.ChatId, executionContext, stoppingToken),
                report.ChatId,
                replyTo,
                "Coding agent report received; continuing...",
                stoppingToken);

            var continuationSnapshot = executionContext.SnapshotData;
            for (var attempt = 0;
                 executionContext.IterationLimitReached &&
                 continuationSnapshot != null &&
                 attempt < Env.CodingAgentMaxAutoResumeContinuations;
                 attempt++) {
                executionContext = new LlmExecutionContext();
                await sendMessageService.SendDraftStream(
                    generalLlmService.ResumeFromSnapshotAsync(continuationSnapshot, executionContext, stoppingToken),
                    report.ChatId,
                    replyTo,
                    "Continuing coding-agent follow-up...",
                    stoppingToken);
                continuationSnapshot = executionContext.SnapshotData;
            }
        }

        private static async Task SendReportMessageAsync(ISendMessageService sendMessageService, CodingAgentJobReport report) {
            var message = BuildReportMessage(report);
            await SendTextAsync(sendMessageService, message, report.ChatId, ToTelegramMessageId(report.MessageId));
        }

        private static string BuildReportMessage(CodingAgentJobReport report) {
            var sb = new StringBuilder();
            sb.AppendLine("Coding agent job finished / 编码任务已结束");
            sb.AppendLine($"JobId: {report.JobId}");
            sb.AppendLine($"Status: {report.Status}");
            sb.AppendLine($"Workspace: {GetPathDisplayName(report.WorkingDirectory)}");
            if (!string.IsNullOrWhiteSpace(report.LogPath)) {
                sb.AppendLine($"Log: {GetPathDisplayName(report.LogPath)}");
            }
            if (!string.IsNullOrWhiteSpace(report.Summary)) {
                sb.AppendLine();
                sb.AppendLine(TrimForTelegram(report.Summary, 1800));
            }
            if (!string.IsNullOrWhiteSpace(report.ErrorMessage)) {
                sb.AppendLine();
                sb.AppendLine("Error:");
                sb.AppendLine(TrimForTelegram(report.ErrorMessage, 1200));
            }
            return sb.ToString();
        }

        private static string BuildContinuationPrompt(CodingAgentJobReport report) {
            var sb = new StringBuilder();
            sb.AppendLine("Background pi coding agent job has finished. Continue the previous Telegram conversation and complete the user's coding request based on this report.");
            sb.AppendLine("后台 pi coding agent 已结束。请基于下面报告继续之前的 Telegram 对话，把剩余 agent loop 跑完并给出最终结论。");
            sb.AppendLine();
            sb.AppendLine($"JobId: {report.JobId}");
            sb.AppendLine($"Status: {report.Status}");
            sb.AppendLine($"Workspace: {GetPathDisplayName(report.WorkingDirectory)}");
            if (!string.IsNullOrWhiteSpace(report.LogPath)) {
                sb.AppendLine($"Log: {GetPathDisplayName(report.LogPath)}");
            }
            sb.AppendLine();
            sb.AppendLine("Original prompt:");
            sb.AppendLine(report.Prompt);
            sb.AppendLine();
            sb.AppendLine("Coding agent summary:");
            sb.AppendLine(string.IsNullOrWhiteSpace(report.Summary) ? "(empty)" : report.Summary);
            if (!string.IsNullOrWhiteSpace(report.Output)) {
                sb.AppendLine();
                sb.AppendLine("Coding agent output:");
                sb.AppendLine(TrimForTelegram(report.Output, 12000));
            }
            if (!string.IsNullOrWhiteSpace(report.ErrorMessage)) {
                sb.AppendLine();
                sb.AppendLine("Coding agent error:");
                sb.AppendLine(report.ErrorMessage);
            }
            return sb.ToString();
        }

        private static async Task<BotIdentity> EnsureBotIdentityAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken) {
            var identityProvider = scopedProvider.GetRequiredService<IBotIdentityProvider>();
            var identity = await identityProvider.GetIdentityAsync();
            if (!string.IsNullOrWhiteSpace(identity.UserName)) {
                return identity;
            }

            var botClient = scopedProvider.GetRequiredService<ITelegramBotClient>();
            var me = await botClient.GetMe(cancellationToken);
            identityProvider.SetIdentity(me.Id, me.Username ?? string.Empty);
            return new BotIdentity(me.Id, me.Username ?? string.Empty);
        }

        private static async Task SendTextAsync(ISendMessageService sendMessageService, string text, long chatId, int replyTo) {
            if (replyTo > 0) {
                await sendMessageService.SplitAndSendTextMessage(text, chatId, replyTo);
            } else {
                await sendMessageService.SendMessage(text, chatId);
            }
        }

        private static int ToTelegramMessageId(long messageId) {
            return messageId is > 0 and <= int.MaxValue ? ( int ) messageId : 0;
        }

        private static string TrimForTelegram(string value, int maxLength) {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
                return value ?? string.Empty;
            }

            return value[..maxLength] + "\n... [truncated]";
        }

        private static string GetPathDisplayName(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return "(redacted)";
            }

            var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var displayName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(displayName) ? "(redacted)" : displayName;
        }
    }
}
