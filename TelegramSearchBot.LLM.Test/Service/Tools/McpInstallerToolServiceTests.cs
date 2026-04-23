#pragma warning disable CS8602
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Mcp;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    /// <summary>
    /// Integration tests for McpInstallerToolService verifying the full add/list/remove/restart flow.
    /// </summary>
    [Collection(McpToolHelperTestCollection.Name)]
    public class McpInstallerToolServiceTests : IDisposable {
        private readonly ServiceProvider _serviceProvider;
        private readonly McpServerManager _mcpServerManager;
        private readonly McpInstallerToolService _service;
        private readonly ToolContext _adminContext;
        private readonly ToolContext _nonAdminContext;
        private readonly string _dbName;

        public McpInstallerToolServiceTests() {
            _dbName = $"McpInstallerTest_{Guid.NewGuid():N}";
            var services = new ServiceCollection();

            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
            services.AddLogging();

            _serviceProvider = services.BuildServiceProvider();

            using (var scope = _serviceProvider.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<DataDbContext>();
                db.Database.EnsureCreated();
            }

            var managerLoggerMock = new Mock<ILogger<McpServerManager>>();
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            _mcpServerManager = new McpServerManager(managerLoggerMock.Object, scopeFactory);

            var serviceLoggerMock = new Mock<ILogger<McpInstallerToolService>>();
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
            _service = new McpInstallerToolService(serviceLoggerMock.Object, _mcpServerManager, redisMock.Object);

            _adminContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };
            _nonAdminContext = new ToolContext { ChatId = 1, UserId = long.MaxValue - 1 };
        }

        public void Dispose() {
            McpToolHelper.RegisterExternalTools(
                new List<(string, McpToolHelper.ExternalToolInfo)>(),
                null);
            _mcpServerManager.Dispose();
            _serviceProvider.Dispose();
        }

        [Fact]
        public async Task ListMcpServers_EmptyState_ReturnsNoServersMessage() {
            var result = await _service.ListMcpServers();
            Assert.Contains("No MCP servers configured", result);
            Assert.Contains("AddMcpServer", result);
        }

        [Fact]
        public async Task AddMcpServer_NonAdmin_ReturnsError() {
            var result = await _service.AddMcpServer("test", "echo", "hello", _nonAdminContext);
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddMcpServer_EmptyName_ReturnsError() {
            var result = await _service.AddMcpServer("", "echo", "hello", _adminContext);
            Assert.Contains("Error", result);
            Assert.Contains("name", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddMcpServer_EmptyCommand_ReturnsError() {
            var result = await _service.AddMcpServer("test", "", "hello", _adminContext);
            Assert.Contains("Error", result);
            Assert.Contains("command", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AddMcpServer_ValidDisabledServer_Success() {
            // Use an invalid command so it doesn't try to actually connect,
            // but the config should still be saved
            var result = await _service.AddMcpServer(
                "test-server",
                "/nonexistent/binary_12345_that_does_not_exist",
                "",
                _adminContext);

            Assert.Contains("Successfully added", result);
            Assert.Contains("test-server", result);
        }

        [Fact]
        public async Task ListMcpServers_AfterAdd_ShowsServer() {
            await _service.AddMcpServer(
                "my-server",
                "/nonexistent/binary_12345_that_does_not_exist",
                "-y @test/server",
                _adminContext);

            var result = await _service.ListMcpServers();
            Assert.Contains("my-server", result);
            Assert.Contains("Configured MCP Servers", result);
        }

        [Fact]
        public async Task RemoveMcpServer_NonAdmin_ReturnsError() {
            var result = await _service.RemoveMcpServer("test", _nonAdminContext);
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RemoveMcpServer_NonExistent_ReturnsNotFound() {
            var result = await _service.RemoveMcpServer("nonexistent", _adminContext);
            Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RemoveMcpServer_ExistingServer_Success() {
            // Add then remove
            await _service.AddMcpServer(
                "remove-me",
                "/nonexistent/binary_12345_that_does_not_exist",
                "",
                _adminContext);

            var result = await _service.RemoveMcpServer("remove-me", _adminContext);
            Assert.Contains("Successfully removed", result);

            // Verify it's gone
            var listResult = await _service.ListMcpServers();
            Assert.Contains("No MCP servers configured", listResult);
        }

        [Fact]
        public async Task RestartMcpServers_NonAdmin_ReturnsError() {
            var result = await _service.RestartMcpServers(_nonAdminContext);
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RestartMcpServers_EmptyState_Success() {
            var result = await _service.RestartMcpServers(_adminContext);
            Assert.Contains("Successfully restarted", result);
        }

        [Fact]
        public async Task FullLifecycle_AddListRemoveList() {
            // 1. Initial state - empty
            var result = await _service.ListMcpServers();
            Assert.Contains("No MCP servers configured", result);

            // 2. Add a server
            result = await _service.AddMcpServer(
                "lifecycle-test",
                "/nonexistent/binary_12345_that_does_not_exist",
                "-y @test/mcp-server",
                _adminContext);
            Assert.Contains("Successfully added", result);

            // 3. List shows the server
            result = await _service.ListMcpServers();
            Assert.Contains("lifecycle-test", result);
            Assert.Contains("Configured MCP Servers (1)", result);

            // 4. Add another server
            result = await _service.AddMcpServer(
                "lifecycle-test-2",
                "/nonexistent/binary_12345_that_does_not_exist",
                "",
                _adminContext);
            Assert.Contains("Successfully added", result);

            // 5. List shows both servers
            result = await _service.ListMcpServers();
            Assert.Contains("lifecycle-test", result);
            Assert.Contains("lifecycle-test-2", result);
            Assert.Contains("Configured MCP Servers (2)", result);

            // 6. Restart
            result = await _service.RestartMcpServers(_adminContext);
            Assert.Contains("Successfully restarted", result);

            // 7. Remove first server
            result = await _service.RemoveMcpServer("lifecycle-test", _adminContext);
            Assert.Contains("Successfully removed", result);

            // 8. List shows only second server
            result = await _service.ListMcpServers();
            Assert.Contains("lifecycle-test-2", result);
            // Verify the first server name doesn't appear as a standalone entry (only as part of "lifecycle-test-2")
            var lines = result.Split('\n');
            Assert.DoesNotContain(lines, line => line.Trim() == "Server: lifecycle-test");
            Assert.Contains("Configured MCP Servers (1)", result);

            // 9. Remove second server
            result = await _service.RemoveMcpServer("lifecycle-test-2", _adminContext);
            Assert.Contains("Successfully removed", result);

            // 10. Back to empty
            result = await _service.ListMcpServers();
            Assert.Contains("No MCP servers configured", result);
        }

        [Fact]
        public async Task AddMcpServer_WithEnvVars_PersistsCorrectly() {
            var result = await _service.AddMcpServer(
                "env-test",
                "/nonexistent/binary_12345_that_does_not_exist",
                "-y @test/server",
                _adminContext,
                "API_KEY=test123;DEBUG=true");

            Assert.Contains("Successfully added", result);

            // Verify env vars were persisted by checking the server config
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.FirstOrDefault(c => c.Name == "env-test");
            Assert.NotNull(config);
            Assert.Equal("test123", config.Env["API_KEY"]);
            Assert.Equal("true", config.Env["DEBUG"]);
        }

        [Fact]
        public async Task AddMcpServer_OverwritesExisting_Success() {
            // Add initial
            await _service.AddMcpServer(
                "overwrite-test",
                "/nonexistent/binary_old",
                "",
                _adminContext);

            // Overwrite with new config
            var result = await _service.AddMcpServer(
                "overwrite-test",
                "/nonexistent/binary_new",
                "-y @new/server",
                _adminContext);

            Assert.Contains("Successfully added", result);

            // Verify the new config
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.FirstOrDefault(c => c.Name == "overwrite-test");
            Assert.NotNull(config);
            Assert.Equal("/nonexistent/binary_new", config.Command);
        }

        [Fact]
        public async Task UpdateMcpServer_NonAdmin_ReturnsError() {
            var result = await _service.UpdateMcpServer("test", _nonAdminContext, timeout: "120");
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UpdateMcpServer_NonExistent_ReturnsNotFound() {
            var result = await _service.UpdateMcpServer("nonexistent", _adminContext, timeout: "120");
            Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UpdateMcpServer_EmptyName_ReturnsError() {
            var result = await _service.UpdateMcpServer("", _adminContext, timeout: "120");
            Assert.Contains("Error", result);
            Assert.Contains("name", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UpdateMcpServer_InvalidTimeout_ReturnsError() {
            await _service.AddMcpServer("timeout-test", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);
            var result = await _service.UpdateMcpServer("timeout-test", _adminContext, timeout: "abc");
            Assert.Contains("Error", result);
            Assert.Contains("positive integer", result);
        }

        [Fact]
        public async Task UpdateMcpServer_NegativeTimeout_ReturnsError() {
            await _service.AddMcpServer("neg-timeout", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);
            var result = await _service.UpdateMcpServer("neg-timeout", _adminContext, timeout: "-5");
            Assert.Contains("Error", result);
        }

        [Fact]
        public async Task UpdateMcpServer_InvalidEnabled_ReturnsError() {
            await _service.AddMcpServer("enabled-test", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);
            var result = await _service.UpdateMcpServer("enabled-test", _adminContext, enabled: "maybe");
            Assert.Contains("Error", result);
            Assert.Contains("'true' or 'false'", result);
        }

        [Fact]
        public async Task UpdateMcpServer_UpdateTimeout_Success() {
            await _service.AddMcpServer("update-timeout", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);

            var result = await _service.UpdateMcpServer("update-timeout", _adminContext, timeout: "120");
            Assert.Contains("Successfully updated", result);
            Assert.Contains("120s", result);

            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.First(c => c.Name == "update-timeout");
            Assert.Equal(120, config.TimeoutSeconds);
        }

        [Fact]
        public async Task UpdateMcpServer_UpdateEnv_MergesVariables() {
            // Add server with initial env var
            await _service.AddMcpServer("update-env", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext, "EXISTING_KEY=original");

            // Update by adding new vars - should merge, not replace
            var result = await _service.UpdateMcpServer("update-env", _adminContext, env: "KEY1=val1;KEY2=val2");
            Assert.Contains("Successfully updated", result);
            Assert.Contains("merged 2 variable(s)", result);

            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.First(c => c.Name == "update-env");
            Assert.Equal("val1", config.Env["KEY1"]);
            Assert.Equal("val2", config.Env["KEY2"]);
            // Original env var should still be there
            Assert.Equal("original", config.Env["EXISTING_KEY"]);
        }

        [Fact]
        public async Task UpdateMcpServer_NoChanges_ReturnsNoChangesMessage() {
            await _service.AddMcpServer("no-change", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);

            var result = await _service.UpdateMcpServer("no-change", _adminContext);
            Assert.Contains("No changes", result);
        }

        [Fact]
        public async Task ListMcpServers_ShowsTimeout() {
            await _service.AddMcpServer("list-timeout", "/nonexistent/binary_12345_that_does_not_exist", "", _adminContext);
            // Update timeout to 120s
            await _service.UpdateMcpServer("list-timeout", _adminContext, timeout: "120");

            var result = await _service.ListMcpServers();
            Assert.Contains("Timeout: 120s", result);
        }
    }
}
