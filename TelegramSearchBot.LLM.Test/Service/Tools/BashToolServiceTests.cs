#pragma warning disable CS8602
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class BashToolServiceTests {
        private readonly Mock<ILogger<BashToolService>> _loggerMock;
        private readonly BashToolService _service;

        public BashToolServiceTests() {
            _loggerMock = new Mock<ILogger<BashToolService>>();
            _service = new BashToolService(_loggerMock.Object);
        }

        [Fact]
        public void GetShellEnvironmentDescription_ReturnsNonEmpty() {
            var description = BashToolService.GetShellEnvironmentDescription();
            Assert.NotNull(description);
            Assert.NotEmpty(description);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Assert.Contains("PowerShell", description);
                Assert.Contains("Windows", description);
            } else {
                Assert.Contains("bash", description);
            }
        }

        [Fact]
        public async Task ExecuteCommand_NonAdminUser_ReturnsError() {
            // Use a userId that is different from the actual AdminId
            var toolContext = new ToolContext { ChatId = 1, UserId = long.MaxValue - 1 };
            var result = await _service.ExecuteCommand("echo test", toolContext);
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteCommand_NullToolContext_ReturnsError() {
            var result = await _service.ExecuteCommand("echo test", null);
            Assert.Contains("Error", result);
        }

        [Fact]
        public async Task ExecuteCommand_EmptyCommand_ReturnsError() {
            // Even with admin user, empty command should fail
            var toolContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };
            var result = await _service.ExecuteCommand("", toolContext);
            Assert.Contains("Error", result);
            Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteCommand_InvalidWorkingDirectory_ReturnsError() {
            var toolContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };
            var result = await _service.ExecuteCommand("echo test", toolContext,
                workingDirectory: "/nonexistent/path/that/doesnt/exist_" + Guid.NewGuid().ToString("N"));
            Assert.Contains("Error", result);
            Assert.Contains("does not exist", result);
        }

        [Fact]
        public async Task ExecuteCommand_AdminUser_ExecutesSuccessfully() {
            var toolContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };

            // Use platform-appropriate command
            string command;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                command = "Write-Output 'hello test'";
            } else {
                command = "echo 'hello test'";
            }

            var result = await _service.ExecuteCommand(command, toolContext,
                workingDirectory: Path.GetTempPath());
            Assert.Contains("Exit code: 0", result);
            Assert.Contains("hello test", result);
        }

        [Fact]
        public async Task ExecuteCommand_TimeoutClamped() {
            var toolContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };

            // Very short timeout should be clamped to 1000ms minimum
            string command;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                command = "Write-Output 'quick'";
            } else {
                command = "echo 'quick'";
            }

            var result = await _service.ExecuteCommand(command, toolContext,
                workingDirectory: Path.GetTempPath(), timeoutMs: 100);
            // Should still execute because clamped to 1s, and echo is fast
            Assert.Contains("Exit code: 0", result);
        }
    }
}

