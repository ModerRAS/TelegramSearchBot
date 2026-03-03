#pragma warning disable CS8602
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.Mcp;
using Xunit;

namespace TelegramSearchBot.Test.Service.Mcp {
    public class McpServerManagerTests : IDisposable {
        private readonly ServiceProvider _serviceProvider;
        private readonly McpServerManager _manager;
        private readonly string _dbName;

        public McpServerManagerTests() {
            _dbName = $"McpServerManagerTest_{Guid.NewGuid():N}";
            var services = new ServiceCollection();

            // Use InMemory database for testing - same name for all scopes
            services.AddDbContext<DataDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.AddLogging();

            _serviceProvider = services.BuildServiceProvider();

            // Ensure database is created
            using (var scope = _serviceProvider.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<DataDbContext>();
                db.Database.EnsureCreated();
            }

            var loggerMock = new Mock<ILogger<McpServerManager>>();
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            _manager = new McpServerManager(loggerMock.Object, scopeFactory);
        }

        public void Dispose() {
            _manager.Dispose();
            _serviceProvider.Dispose();
        }

        [Fact]
        public async Task GetServerConfigsAsync_EmptyDb_ReturnsEmptyList() {
            var configs = await _manager.GetServerConfigsAsync();
            Assert.NotNull(configs);
            Assert.Empty(configs);
        }

        [Fact]
        public async Task AddServerAsync_SavesConfigToDb() {
            var config = new McpServerConfig {
                Name = "test-server",
                Command = "npx",
                Args = new() { "-y", "@test/server" },
                Enabled = false, // Don't try to connect
            };

            await _manager.AddServerAsync(config);

            // Verify it's saved in the database
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataDbContext>();
            var item = await db.AppConfigurationItems.FindAsync("MCP:ServerConfig:test-server");
            Assert.NotNull(item);
            Assert.Contains("test-server", item.Value);
            Assert.Contains("npx", item.Value);
        }

        [Fact]
        public async Task GetServerConfigsAsync_ReturnsAddedConfigs() {
            var config = new McpServerConfig {
                Name = "my-server",
                Command = "node",
                Args = new() { "server.js" },
                Enabled = false,
            };

            await _manager.AddServerAsync(config);

            var configs = await _manager.GetServerConfigsAsync();
            Assert.Single(configs);
            Assert.Equal("my-server", configs[0].Name);
            Assert.Equal("node", configs[0].Command);
        }

        [Fact]
        public async Task AddServerAsync_OverwritesExistingConfig() {
            var config1 = new McpServerConfig {
                Name = "dup-server",
                Command = "old-command",
                Enabled = false,
            };
            await _manager.AddServerAsync(config1);

            var config2 = new McpServerConfig {
                Name = "dup-server",
                Command = "new-command",
                Enabled = false,
            };
            await _manager.AddServerAsync(config2);

            var configs = await _manager.GetServerConfigsAsync();
            Assert.Single(configs);
            Assert.Equal("new-command", configs[0].Command);
        }

        [Fact]
        public async Task RemoveServerAsync_RemovesConfigFromDb() {
            var config = new McpServerConfig {
                Name = "remove-me",
                Command = "test",
                Enabled = false,
            };
            await _manager.AddServerAsync(config);

            // Verify it exists
            var configs = await _manager.GetServerConfigsAsync();
            Assert.Single(configs);

            await _manager.RemoveServerAsync("remove-me");

            configs = await _manager.GetServerConfigsAsync();
            Assert.Empty(configs);
        }

        [Fact]
        public async Task RemoveServerAsync_NonExistent_DoesNotThrow() {
            await _manager.RemoveServerAsync("nonexistent");
        }

        [Fact]
        public async Task AddServerAsync_WithEnvVars_PersistsCorrectly() {
            var config = new McpServerConfig {
                Name = "env-test",
                Command = "npx",
                Env = new() { { "API_KEY", "secret123" }, { "DEBUG", "true" } },
                Enabled = false,
            };
            await _manager.AddServerAsync(config);

            var configs = await _manager.GetServerConfigsAsync();
            Assert.Single(configs);
            Assert.Equal(2, configs[0].Env.Count);
            Assert.Equal("secret123", configs[0].Env["API_KEY"]);
            Assert.Equal("true", configs[0].Env["DEBUG"]);
        }

        [Fact]
        public void GetAllExternalTools_NoServers_ReturnsEmpty() {
            var tools = _manager.GetAllExternalTools();
            Assert.NotNull(tools);
            Assert.Empty(tools);
        }

        [Fact]
        public async Task AddServerAsync_NullConfig_ThrowsArgumentNullException() {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _manager.AddServerAsync(null));
        }

        [Fact]
        public async Task AddServerAsync_EmptyName_ThrowsArgumentException() {
            var config = new McpServerConfig { Name = "", Command = "test" };
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.AddServerAsync(config));
        }
    }
}
