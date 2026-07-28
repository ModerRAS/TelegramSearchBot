using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    [Collection("AgentEnvSerial")]
    public sealed class CodingAgentToolServiceTests {
        [Fact]
        public void PathPolicy_AcceptsExistingDirectoryAndRejectsDeniedPrefix() {
            var originalDeniedPrefixes = Env.CodingAgentDeniedPathPrefixes;
            var tempDir = CreateTempDirectory();

            try {
                Env.CodingAgentDeniedPathPrefixes = [];
                var policy = new CodingAgentPathPolicy(NullLogger<CodingAgentPathPolicy>.Instance);
                var accepted = policy.ValidateWorkspace(tempDir);
                Assert.True(accepted.IsValid);
                Assert.Equal(Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), accepted.FullPath);

                Env.CodingAgentDeniedPathPrefixes = [tempDir];
                var rejected = policy.ValidateWorkspace(tempDir);
                Assert.False(rejected.IsValid);
                Assert.Contains("denied", rejected.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            } finally {
                Env.CodingAgentDeniedPathPrefixes = originalDeniedPrefixes;
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task JobService_EnqueueAsync_PersistsStateAndQueuesPayload() {
            var originalMaxConcurrentJobs = Env.CodingAgentMaxConcurrentJobs;
            Env.CodingAgentMaxConcurrentJobs = 2;

            try {
                var redis = new InMemoryRedisTestHarness();
                var service = new CodingAgentJobService(redis.Connection.Object, NullLogger<CodingAgentJobService>.Instance);

                var request = new CodingAgentJobRequest {
                    JobId = "ca_test",
                    ChatId = -100,
                    UserId = 42,
                    MessageId = 99,
                    Prompt = "fix it",
                    WorkingDirectory = "D:\\repo",
                    TimeoutMinutes = 5
                };

                await service.EnqueueAsync(request);

                var queuedPayload = redis.PeekFirstListValue(LlmAgentRedisKeys.CodingAgentJobQueue);
                Assert.NotNull(queuedPayload);
                Assert.Contains("\"JobId\":\"ca_test\"", queuedPayload);

                var state = redis.GetHash(LlmAgentRedisKeys.CodingAgentJobState("ca_test"));
                Assert.Equal(CodingAgentJobStatus.Pending.ToString(), state["status"]);
                Assert.Equal("-100", state["chatId"]);
                Assert.Contains("ca_test", redis.GetSetValues(LlmAgentRedisKeys.CodingAgentActiveJobSet));
            } finally {
                Env.CodingAgentMaxConcurrentJobs = originalMaxConcurrentJobs;
            }
        }

        [Fact]
        public async Task RunCodingAgentAsync_RequiresWhitelistAndQueuesAllowedChat() {
            var originalEnabled = Env.EnableCodingAgentTool;
            var originalAllowedGroups = Env.CodingAgentAllowedGroupIds;
            var originalDeniedPrefixes = Env.CodingAgentDeniedPathPrefixes;
            var originalMaxConcurrentJobs = Env.CodingAgentMaxConcurrentJobs;
            var tempDir = CreateTempDirectory();

            try {
                Env.EnableCodingAgentTool = true;
                Env.CodingAgentAllowedGroupIds = [-100];
                Env.CodingAgentDeniedPathPrefixes = [];
                Env.CodingAgentMaxConcurrentJobs = 2;

                var redis = new InMemoryRedisTestHarness();
                var jobService = new CodingAgentJobService(redis.Connection.Object, NullLogger<CodingAgentJobService>.Instance);
                var pathPolicy = new CodingAgentPathPolicy(NullLogger<CodingAgentPathPolicy>.Instance);
                var tool = new CodingAgentToolService(pathPolicy, jobService, NullLogger<CodingAgentToolService>.Instance);

                var denied = await tool.RunCodingAgentAsync(
                    "fix it",
                    tempDir,
                    new ToolContext { ChatId = -200, UserId = 42, MessageId = 99 });
                Assert.Contains("not whitelisted", denied, StringComparison.OrdinalIgnoreCase);

                var allowed = await tool.RunCodingAgentAsync(
                    "fix it",
                    tempDir,
                    new ToolContext { ChatId = -100, UserId = 42, MessageId = 99 },
                    timeoutMinutes: 3);

                var json = JObject.Parse(allowed);
                Assert.Equal(CodingAgentJobStatus.Pending.ToString(), json["status"]?.ToString());
                Assert.Equal(3, json["timeoutMinutes"]?.Value<int>());
                Assert.Single(redis.GetListValues(LlmAgentRedisKeys.CodingAgentJobQueue));
            } finally {
                Env.EnableCodingAgentTool = originalEnabled;
                Env.CodingAgentAllowedGroupIds = originalAllowedGroups;
                Env.CodingAgentDeniedPathPrefixes = originalDeniedPrefixes;
                Env.CodingAgentMaxConcurrentJobs = originalMaxConcurrentJobs;
                Directory.Delete(tempDir, recursive: true);
            }
        }

        private static string CreateTempDirectory() {
            var path = Path.Combine(Path.GetTempPath(), $"tgsb-coding-agent-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
