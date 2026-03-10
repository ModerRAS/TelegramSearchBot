#pragma warning disable CS8602
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.Mcp;
using Xunit;

namespace TelegramSearchBot.Test.Service.Mcp {
    public class McpClientTests : IDisposable {
        private readonly Mock<ILogger> _loggerMock;

        public McpClientTests() {
            _loggerMock = new Mock<ILogger>();
        }

        public void Dispose() {
        }

        [Fact]
        public void Constructor_NullConfig_ThrowsArgumentNullException() {
            Assert.Throws<ArgumentNullException>(() => new McpClient(null, _loggerMock.Object));
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            Assert.Throws<ArgumentNullException>(() => new McpClient(config, null));
        }

        [Fact]
        public void ServerName_ReturnsConfigName() {
            var config = new McpServerConfig { Name = "my-server", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);
            Assert.Equal("my-server", client.ServerName);
        }

        [Fact]
        public void IsConnected_InitiallyFalse() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void IsProcessAlive_NoProcess_ReturnsFalse() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);
            Assert.False(client.IsProcessAlive);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task ListToolsAsync_NotConnected_ThrowsInvalidOperationException() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync());
        }

        [Fact]
        public async Task CallToolAsync_NotConnected_ThrowsInvalidOperationException() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.CallToolAsync("someTool", new Dictionary<string, object>()));
        }

        [Fact]
        public async Task DisconnectAsync_NotConnected_DoesNotThrow() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            await client.DisconnectAsync(); // Should not throw
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void Dispose_NotConnected_DoesNotThrow() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            client.Dispose(); // Should not throw
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            client.Dispose();
            client.Dispose(); // Second dispose should not throw
        }

        [Fact]
        public async Task ConnectAsync_InvalidCommand_ThrowsAndCleansUp() {
            var config = new McpServerConfig {
                Name = "bad-server",
                Command = "/nonexistent/path/to/binary_that_does_not_exist_12345"
            };
            var client = new McpClient(config, _loggerMock.Object);

            // Should throw because the process can't be started
            await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());
            Assert.False(client.IsConnected);
            Assert.False(client.IsProcessAlive);
        }

        [Fact]
        public async Task ConnectAsync_ProcessExitsImmediately_IsNotConnected() {
            // Use a command that exits immediately (not a valid MCP server)
            var config = new McpServerConfig {
                Name = "exit-server",
                Command = "echo",
                Args = new List<string> { "hello" }
            };
            var client = new McpClient(config, _loggerMock.Object);

            // The process will exit immediately and won't respond to the MCP handshake
            // so ConnectAsync should fail (timeout or read error)
            await Assert.ThrowsAnyAsync<Exception>(() =>
                client.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token));
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task HeartbeatAsync_NotConnected_ReturnsFalse() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            var result = await client.HeartbeatAsync();
            Assert.False(result);
        }

        [Fact]
        public async Task HeartbeatAsync_NotProcessAlive_ReturnsFalse() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            // Manually set connected but process is dead (simulated via IsProcessAlive check)
            // Since IsProcessAlive checks _process.HasExited and _process is null, it returns false
            var result = await client.HeartbeatAsync();
            Assert.False(result);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task HeartbeatAsync_AfterDisconnect_ReturnsFalse() {
            var config = new McpServerConfig { Name = "test", Command = "echo" };
            var client = new McpClient(config, _loggerMock.Object);

            await client.DisconnectAsync();

            var result = await client.HeartbeatAsync();
            Assert.False(result);
        }
    }
}
