using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public sealed class CodingAgentToolService : IService {
        private readonly CodingAgentPathPolicy _pathPolicy;
        private readonly CodingAgentJobService _jobService;
        private readonly ILogger<CodingAgentToolService> _logger;

        public CodingAgentToolService(
            CodingAgentPathPolicy pathPolicy,
            CodingAgentJobService jobService,
            ILogger<CodingAgentToolService> logger) {
            _pathPolicy = pathPolicy;
            _jobService = jobService;
            _logger = logger;
        }

        public string ServiceName => nameof(CodingAgentToolService);

        [BuiltInTool(
            "Start a background pi coding agent job. Returns immediately with a job id; the final report is posted back to Telegram and the LLM loop is resumed automatically. / 启动后台 pi coding agent 任务，立即返回 job id；结束后会把报告发回 Telegram 并自动续跑 LLM。",
            Name = "run_coding_agent")]
        public async Task<string> RunCodingAgentAsync(
            [BuiltInParameter("Coding task prompt for pi. / 交给 pi 的编码任务说明。")] string prompt,
            [BuiltInParameter("Existing workspace directory where pi should run. / pi 运行的现有工作目录。")] string workingDirectory,
            ToolContext toolContext,
            [BuiltInParameter("Optional JSON array of agents, e.g. [{\"name\":\"reviewer\",\"prompt\":\"review the change\"}]. / 可选 multi-agent JSON 配置。", IsRequired = false)] string agents = "",
            [BuiltInParameter("Optional timeout in minutes. / 可选超时时间（分钟）。", IsRequired = false)] int? timeoutMinutes = null,
            [BuiltInParameter("Optional pi provider name. / 可选 pi provider。", IsRequired = false)] string provider = "",
            [BuiltInParameter("Optional pi model pattern or id. / 可选 pi model。", IsRequired = false)] string model = "",
            [BuiltInParameter("Optional pi tools option value. / 可选 pi tools 参数。", IsRequired = false)] string tools = "") {
            var accessError = ValidateToolAccess(toolContext);
            if (!string.IsNullOrWhiteSpace(accessError)) {
                return accessError;
            }

            if (string.IsNullOrWhiteSpace(prompt)) {
                return "Error: prompt is required. / 错误：prompt 不能为空。";
            }

            var workspace = _pathPolicy.ValidateWorkspace(workingDirectory);
            if (!workspace.IsValid) {
                return $"Error: invalid workingDirectory. / 错误：workingDirectory 不可用。\n{workspace.ErrorMessage}";
            }

            var effectiveTimeout = timeoutMinutes.HasValue && timeoutMinutes.Value > 0
                ? Math.Clamp(timeoutMinutes.Value, 1, 1440)
                : Env.CodingAgentDefaultTimeoutMinutes;

            try {
                var request = await _jobService.EnqueueAsync(new CodingAgentJobRequest {
                    JobId = $"ca_{Guid.NewGuid():N}",
                    ChatId = toolContext.ChatId,
                    UserId = toolContext.UserId,
                    MessageId = toolContext.MessageId,
                    Prompt = prompt.Trim(),
                    WorkingDirectory = workspace.FullPath,
                    AgentsJson = agents?.Trim() ?? string.Empty,
                    TimeoutMinutes = effectiveTimeout,
                    Provider = provider?.Trim() ?? string.Empty,
                    Model = model?.Trim() ?? string.Empty,
                    Tools = tools?.Trim() ?? string.Empty
                });

                return JsonConvert.SerializeObject(new {
                    jobId = request.JobId,
                    status = CodingAgentJobStatus.Pending.ToString(),
                    workingDirectory = request.WorkingDirectory,
                    timeoutMinutes = request.TimeoutMinutes,
                    message = "已排队并在后台运行。Queued; the report will be posted back and the LLM loop will resume automatically."
                }, Formatting.Indented);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to enqueue coding agent job. ChatId={ChatId}, UserId={UserId}", toolContext.ChatId, toolContext.UserId);
                return $"Error: failed to enqueue coding agent job. / 错误：无法排队 coding agent 任务。\n{ex.GetLogSummary()}";
            }
        }

        [BuiltInTool("Get status for a background pi coding agent job. / 查询后台 pi coding agent 任务状态。", Name = "get_coding_agent_job")]
        public async Task<string> GetCodingAgentJobAsync(
            [BuiltInParameter("Coding agent job id returned by run_coding_agent. / run_coding_agent 返回的 job id。")] string jobId,
            ToolContext toolContext) {
            var accessError = ValidateToolAccess(toolContext);
            if (!string.IsNullOrWhiteSpace(accessError)) {
                return accessError;
            }

            var state = await _jobService.GetStateAsync(jobId);
            if (state.Count == 0) {
                return "Error: job not found. / 错误：找不到该 job。";
            }

            if (!IsSameChat(state, toolContext.ChatId)) {
                return "Error: job belongs to a different chat. / 错误：该 job 属于其他群聊。";
            }

            return JsonConvert.SerializeObject(state, Formatting.Indented);
        }

        [BuiltInTool("Request cancellation for a background pi coding agent job. / 请求取消后台 pi coding agent 任务。", Name = "cancel_coding_agent_job")]
        public async Task<string> CancelCodingAgentJobAsync(
            [BuiltInParameter("Coding agent job id returned by run_coding_agent. / run_coding_agent 返回的 job id。")] string jobId,
            ToolContext toolContext,
            [BuiltInParameter("Optional cancellation reason. / 可选取消原因。", IsRequired = false)] string reason = "") {
            var accessError = ValidateToolAccess(toolContext);
            if (!string.IsNullOrWhiteSpace(accessError)) {
                return accessError;
            }

            var cancelled = await _jobService.RequestCancelAsync(jobId, toolContext.ChatId, toolContext.UserId, reason);
            if (!cancelled) {
                return "Error: job not found or cannot be cancelled from this chat. / 错误：找不到 job，或当前群聊无权取消。";
            }

            return JsonConvert.SerializeObject(new {
                jobId = jobId.Trim(),
                status = CodingAgentJobStatus.Cancelling.ToString(),
                message = "Cancellation requested. / 已请求取消。"
            }, Formatting.Indented);
        }

        private static string ValidateToolAccess(ToolContext toolContext) {
            if (!Env.EnableCodingAgentTool) {
                return "Error: coding agent tool is disabled. / 错误：coding agent 工具未启用。";
            }

            if (toolContext == null || toolContext.ChatId == 0) {
                return "Error: coding agent tool requires Telegram chat context. / 错误：coding agent 工具需要 Telegram 群聊上下文。";
            }

            if (!Env.CodingAgentAllowedGroupIds.Contains(toolContext.ChatId)) {
                return "Error: this chat is not whitelisted for coding agent jobs. / 错误：当前群聊不在 coding agent 白名单内。";
            }

            return string.Empty;
        }

        private static bool IsSameChat(IReadOnlyDictionary<string, string> state, long chatId) {
            return state.TryGetValue("chatId", out var storedChatId) &&
                   long.TryParse(storedChatId, out var parsedChatId) &&
                   parsedChatId == chatId;
        }
    }
}
