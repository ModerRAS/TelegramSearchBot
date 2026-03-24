#pragma warning disable CS8602
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.Manage;
using Xunit;

namespace TelegramSearchBot.Test.Manage {
    public class EditMcpConfServiceTests {
        private readonly Mock<IMcpServerManager> _mcpServerManagerMock;
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly EditMcpConfService _service;

        public EditMcpConfServiceTests() {
            _mcpServerManagerMock = new Mock<IMcpServerManager>();
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);

            // Default: accept all Redis writes
            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<When>()))
                .ReturnsAsync(true);
            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _dbMock.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Default: no servers configured
            _mcpServerManagerMock.Setup(m => m.GetServerConfigsAsync())
                .ReturnsAsync(new List<McpServerConfig>());
            _mcpServerManagerMock.Setup(m => m.GetAllExternalTools())
                .Returns(new List<(string, McpToolDescription)>());

            var loggerMock = new Mock<ILogger<EditMcpConfService>>();
            _service = new EditMcpConfService(
                _mcpServerManagerMock.Object,
                loggerMock.Object,
                _redisMock.Object);
        }

        private void SetupServers(params McpServerConfig[] configs) {
            _mcpServerManagerMock.Setup(m => m.GetServerConfigsAsync())
                .ReturnsAsync(configs.ToList());
        }

        [Fact]
        public async Task ListServers_Empty_ShowsHelpMessage() {
            var (status, message) = await _service.ExecuteAsync("查看MCP服务器", 12345);
            Assert.True(status);
            Assert.Contains("当前没有已配置的MCP服务器", message);
            Assert.Contains("新建MCP服务器", message);
        }

        [Fact]
        public async Task ListServers_WithServers_ShowsInfoWithoutEnvValues() {
            SetupServers(new McpServerConfig {
                Name = "test-server",
                Command = "npx",
                Args = new List<string> { "-y", "@test/server" },
                Enabled = true,
                TimeoutSeconds = 60,
                Env = new Dictionary<string, string> { { "API_KEY", "sk-super-secret" } }
            });

            var (status, message) = await _service.ExecuteAsync("查看MCP服务器", 12345);
            Assert.True(status);
            Assert.Contains("test-server", message);
            Assert.Contains("npx", message);
            Assert.Contains("60s", message);
            Assert.Contains("API_KEY", message);
            Assert.DoesNotContain("sk-super-secret", message);
        }

        [Fact]
        public async Task AddServer_CompleteFlow() {
            long chatId = 100;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_adding_name")
                .ReturnsAsync("mcp_adding_command")
                .ReturnsAsync("mcp_adding_args")
                .ReturnsAsync("mcp_adding_env_key")
                .ReturnsAsync("mcp_adding_env_value")
                .ReturnsAsync("mcp_adding_env_key")
                .ReturnsAsync("mcp_adding_timeout");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("my-server")                                              // call 3: HandleAddingCommandAsync
                .ReturnsAsync("my-server|uvx")                                          // call 4: HandleAddingArgsAsync
                .ReturnsAsync("my-server|uvx|minimax-mcp")                              // call 5: HandleAddingEnvKeyAsync("API_KEY")
                .ReturnsAsync("my-server|uvx|minimax-mcp|env:API_KEY")                  // call 6: HandleAddingEnvValueAsync
                .ReturnsAsync("my-server|uvx|minimax-mcp|env:API_KEY=secret123");       // call 8: HandleAddingTimeoutAsync

            _mcpServerManagerMock.Setup(m => m.AddServerAsync(It.IsAny<McpServerConfig>()))
                .Returns(Task.CompletedTask);

            var (s1, m1) = await _service.ExecuteAsync("新建MCP服务器", chatId);
            Assert.True(s1);
            Assert.Contains("名称", m1);

            var (s2, m2) = await _service.ExecuteAsync("my-server", chatId);
            Assert.True(s2);
            Assert.Contains("命令", m2);

            var (s3, m3) = await _service.ExecuteAsync("uvx", chatId);
            Assert.True(s3);
            Assert.Contains("参数", m3);

            var (s4, m4) = await _service.ExecuteAsync("minimax-mcp", chatId);
            Assert.True(s4);
            Assert.Contains("环境变量", m4);

            var (s5a, m5a) = await _service.ExecuteAsync("API_KEY", chatId);
            Assert.True(s5a);
            Assert.Contains("值", m5a);

            var (s5b, m5b) = await _service.ExecuteAsync("secret123", chatId);
            Assert.True(s5b);
            Assert.Contains("继续", m5b);

            var (s6, m6) = await _service.ExecuteAsync("完成", chatId);
            Assert.True(s6);
            Assert.Contains("超时", m6);

            var (s7, m7) = await _service.ExecuteAsync("120", chatId);
            Assert.True(s7);
            Assert.Contains("创建成功", m7);
            Assert.Contains("my-server", m7);

            _mcpServerManagerMock.Verify(m => m.AddServerAsync(It.Is<McpServerConfig>(c =>
                c.Name == "my-server" &&
                c.Command == "uvx" &&
                c.Args.Contains("minimax-mcp") &&
                c.TimeoutSeconds == 120 &&
                c.Env["API_KEY"] == "secret123"
            )), Times.Once);
        }

        [Fact]
        public async Task AddServer_SkipArgsAndEnv() {
            long chatId = 200;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_adding_name")
                .ReturnsAsync("mcp_adding_command")
                .ReturnsAsync("mcp_adding_args")
                .ReturnsAsync("mcp_adding_env_key")
                .ReturnsAsync("mcp_adding_timeout");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("simple-server")          // call 3: HandleAddingCommandAsync
                .ReturnsAsync("simple-server|echo")     // call 4: HandleAddingArgsAsync
                .ReturnsAsync("simple-server|echo|");   // call 6: HandleAddingTimeoutAsync

            _mcpServerManagerMock.Setup(m => m.AddServerAsync(It.IsAny<McpServerConfig>()))
                .Returns(Task.CompletedTask);

            await _service.ExecuteAsync("新建MCP服务器", chatId);
            await _service.ExecuteAsync("simple-server", chatId);
            await _service.ExecuteAsync("echo", chatId);
            await _service.ExecuteAsync("-", chatId);
            await _service.ExecuteAsync("完成", chatId);
            var (status, message) = await _service.ExecuteAsync("", chatId);
            Assert.True(status);
            Assert.Contains("创建成功", message);

            _mcpServerManagerMock.Verify(m => m.AddServerAsync(It.Is<McpServerConfig>(c =>
                c.Name == "simple-server" &&
                c.Command == "echo" &&
                c.Args.Count == 0 &&
                c.TimeoutSeconds == 30
            )), Times.Once);
        }

        [Fact]
        public async Task AddServer_DuplicateName_RejectsAndAsksAgain() {
            long chatId = 300;
            var stateKey = $"mcpconf:{chatId}:state";

            SetupServers(new McpServerConfig { Name = "existing", Command = "test" });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_adding_name");

            await _service.ExecuteAsync("新建MCP服务器", chatId);
            var (status, message) = await _service.ExecuteAsync("existing", chatId);
            Assert.True(status);
            Assert.Contains("已存在", message);
        }

        [Fact]
        public async Task EditServer_SelectAndUpdateTimeout() {
            long chatId = 400;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            SetupServers(new McpServerConfig {
                Name = "edit-test", Command = "npx",
                Enabled = true, TimeoutSeconds = 30
            });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_editing_select_server")
                .ReturnsAsync("mcp_editing_select_field")
                .ReturnsAsync("mcp_editing_input_value");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("edit-test")              // call 3: HandleEditingSelectFieldAsync
                .ReturnsAsync("edit-test|4");           // call 4: HandleEditingInputValueAsync

            _mcpServerManagerMock.Setup(m => m.UpdateServerConfigAsync(
                "edit-test", It.IsAny<Action<McpServerConfig>>()))
                .Callback<string, Action<McpServerConfig>>((name, patch) => {
                    var config = new McpServerConfig { Name = name, TimeoutSeconds = 30 };
                    patch(config);
                })
                .Returns(Task.CompletedTask);

            var (s1, m1) = await _service.ExecuteAsync("编辑MCP服务器", chatId);
            Assert.True(s1);
            Assert.Contains("edit-test", m1);

            var (s2, m2) = await _service.ExecuteAsync("edit-test", chatId);
            Assert.True(s2);
            Assert.Contains("超时", m2);

            var (s3, m3) = await _service.ExecuteAsync("4", chatId);
            Assert.True(s3);

            var (s4, m4) = await _service.ExecuteAsync("120", chatId);
            Assert.True(s4);
            Assert.Contains("更新", m4);
        }

        [Fact]
        public async Task EditServer_WriteOnlyEnvVar() {
            long chatId = 500;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            SetupServers(new McpServerConfig {
                Name = "env-test", Command = "npx",
                Env = new Dictionary<string, string> { { "SECRET", "hidden-value" } }
            });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_editing_select_server")
                .ReturnsAsync("mcp_editing_select_field")
                .ReturnsAsync("mcp_editing_env_key")
                .ReturnsAsync("mcp_editing_env_value");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("env-test")               // call 3: HandleEditingSelectFieldAsync
                .ReturnsAsync("env-test|3")             // call 4: HandleEditingEnvKeyAsync
                .ReturnsAsync("env-test|3|NEW_KEY");    // call 5: HandleEditingEnvValueAsync

            _mcpServerManagerMock.Setup(m => m.UpdateServerConfigAsync(
                "env-test", It.IsAny<Action<McpServerConfig>>()))
                .Returns(Task.CompletedTask)
                .Callback<string, Action<McpServerConfig>>((name, patch) => {
                    var config = new McpServerConfig {
                        Name = name,
                        Env = new Dictionary<string, string> { { "SECRET", "hidden-value" } }
                    };
                    patch(config);
                    Assert.Equal("new-value", config.Env["NEW_KEY"]);
                    Assert.Equal("hidden-value", config.Env["SECRET"]);
                });

            await _service.ExecuteAsync("编辑MCP服务器", chatId);
            await _service.ExecuteAsync("env-test", chatId);
            await _service.ExecuteAsync("3", chatId);
            var (s4, m4) = await _service.ExecuteAsync("NEW_KEY", chatId);
            Assert.True(s4);
            Assert.Contains("值", m4);
            var (s5, m5) = await _service.ExecuteAsync("new-value", chatId);
            Assert.True(s5);
            Assert.Contains("已设置", m5);
        }

        [Fact]
        public async Task DeleteServer_ConfirmFlow() {
            long chatId = 600;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            SetupServers(new McpServerConfig { Name = "delete-me", Command = "test" });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_deleting_select_server")
                .ReturnsAsync("mcp_deleting_confirm");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("delete-me");             // call 3: HandleDeletingConfirmAsync

            _mcpServerManagerMock.Setup(m => m.RemoveServerAsync("delete-me"))
                .Returns(Task.CompletedTask);

            var (s1, m1) = await _service.ExecuteAsync("删除MCP服务器", chatId);
            Assert.True(s1);
            Assert.Contains("delete-me", m1);

            var (s2, m2) = await _service.ExecuteAsync("delete-me", chatId);
            Assert.True(s2);
            Assert.Contains("确认", m2);

            var (s3, m3) = await _service.ExecuteAsync("是", chatId);
            Assert.True(s3);
            Assert.Contains("已删除", m3);
            _mcpServerManagerMock.Verify(m => m.RemoveServerAsync("delete-me"), Times.Once);
        }

        [Fact]
        public async Task DeleteServer_CancelFlow() {
            long chatId = 700;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            SetupServers(new McpServerConfig { Name = "keep-me", Command = "test" });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_deleting_select_server")
                .ReturnsAsync("mcp_deleting_confirm");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("keep-me");               // call 3: HandleDeletingConfirmAsync

            await _service.ExecuteAsync("删除MCP服务器", chatId);
            await _service.ExecuteAsync("keep-me", chatId);
            var (status, message) = await _service.ExecuteAsync("否", chatId);
            Assert.True(status);
            Assert.Contains("取消", message);
            _mcpServerManagerMock.Verify(m => m.RemoveServerAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RestartServers_Success() {
            _mcpServerManagerMock.Setup(m => m.ShutdownAllAsync()).Returns(Task.CompletedTask);
            _mcpServerManagerMock.Setup(m => m.InitializeAllServersAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var (status, message) = await _service.ExecuteAsync("重启MCP服务器", 12345);
            Assert.True(status);
            Assert.Contains("重启", message);
        }

        [Fact]
        public async Task EditServer_NonExistentServer_RejectsAndAsksAgain() {
            long chatId = 800;
            var stateKey = $"mcpconf:{chatId}:state";

            SetupServers(new McpServerConfig { Name = "real-server", Command = "test" });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_editing_select_server");

            await _service.ExecuteAsync("编辑MCP服务器", chatId);
            var (status, message) = await _service.ExecuteAsync("nonexistent", chatId);
            Assert.True(status);
            Assert.Contains("未找到", message);
        }

        [Fact]
        public async Task EditServer_InvalidFieldNumber_RejectsAndAsksAgain() {
            long chatId = 900;
            var stateKey = $"mcpconf:{chatId}:state";
            var dataKey = $"mcpconf:{chatId}:data";

            SetupServers(new McpServerConfig { Name = "field-test", Command = "test" });

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)stateKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null)
                .ReturnsAsync("mcp_editing_select_server")
                .ReturnsAsync("mcp_editing_select_field");

            _dbMock.SetupSequence(d => d.StringGetAsync((RedisKey)dataKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync("field-test");            // call 3: HandleEditingSelectFieldAsync

            await _service.ExecuteAsync("编辑MCP服务器", chatId);
            await _service.ExecuteAsync("field-test", chatId);
            var (status, message) = await _service.ExecuteAsync("99", chatId);
            Assert.True(status);
            Assert.Contains("1-5", message);
        }

        [Fact]
        public async Task UnknownCommand_NoState_ReturnsFalse() {
            var (status, message) = await _service.ExecuteAsync("unknown command", 12345);
            Assert.False(status);
        }

        [Fact]
        public async Task ListServers_EnvKeysOnly_NeverShowsValues() {
            SetupServers(new McpServerConfig {
                Name = "secret-server", Command = "test",
                Env = new Dictionary<string, string> {
                    { "API_KEY", "sk-super-secret-key-12345" },
                    { "DB_PASS", "my-database-password" }
                }
            });

            var (status, message) = await _service.ExecuteAsync("查看MCP服务器", 12345);
            Assert.True(status);
            Assert.Contains("API_KEY", message);
            Assert.Contains("DB_PASS", message);
            Assert.DoesNotContain("sk-super-secret-key-12345", message);
            Assert.DoesNotContain("my-database-password", message);
        }
    }
}
