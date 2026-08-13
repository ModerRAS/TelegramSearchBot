using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using TelegramSearchBot.Common;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM;

[SupportedOSPlatform("windows")]
[Collection("AgentEnvSerial")]
public class WindowsAppContainerToolHostServiceTests {
    [Fact]
    public void BuildProfileName_IsStableAndValid() {
        var first = WindowsAppContainerToolHostService.BuildProfileName(12345, "TelegramSearchBot.Chat.");
        var second = WindowsAppContainerToolHostService.BuildProfileName(12345, "TelegramSearchBot.Chat.");

        Assert.Equal(first, second);
        Assert.StartsWith("TelegramSearchBot.Chat.", first, StringComparison.Ordinal);
        Assert.All(first, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'));
    }

    [Theory]
    [InlineData("bad prefix ")]
    [InlineData("沙箱.")]
    public void BuildProfileName_RejectsInvalidPrefix(string prefix) {
        Assert.Throws<InvalidOperationException>(() =>
            WindowsAppContainerToolHostService.BuildProfileName(12345, prefix));
    }

    [Fact]
    public void BuildPathPolicy_ReusesOriginalSandboxiePathSet() {
        var originalGroupRoot = Env.SandboxieGroupFilesRoot;
        var originalReadPaths = Env.SandboxieGlobalReadPaths;
        var customRoot = Path.Combine(Path.GetTempPath(), "TGSB_GroupFiles_" + Guid.NewGuid().ToString("N"));
        var globalRead = Path.Combine(Path.GetTempPath(), "TGSB_GlobalRead_" + Guid.NewGuid().ToString("N"));
        Env.SandboxieGroupFilesRoot = customRoot;
        Env.SandboxieGlobalReadPaths = [globalRead];
        try {
            var chatId = 67890L;
            var policy = WindowsAppContainerToolHostService.BuildPathPolicy(chatId);

            Assert.Contains(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), policy.ReadOnlyPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(globalRead), policy.ReadOnlyPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(customRoot, chatId.ToString()), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(Env.WorkDir, "Photos", chatId.ToString()), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(Env.WorkDir, "Audios", chatId.ToString()), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(Env.WorkDir, "Videos", chatId.ToString()), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(Env.WorkDir, "Files", chatId.ToString()), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(customRoot, chatId.ToString()), policy.DefaultWorkingDirectory, ignoreCase: true);
            Assert.DoesNotContain(Path.Combine(customRoot, "111"), policy.WritablePaths, StringComparer.OrdinalIgnoreCase);
        } finally {
            Env.SandboxieGroupFilesRoot = originalGroupRoot;
            Env.SandboxieGlobalReadPaths = originalReadPaths;
        }
    }

    [Fact]
    public void BuildPathPolicy_UsesChatFilesAsDefaultWhenGroupRootIsEmpty() {
        var originalGroupRoot = Env.SandboxieGroupFilesRoot;
        Env.SandboxieGroupFilesRoot = string.Empty;
        try {
            var policy = WindowsAppContainerToolHostService.BuildPathPolicy(24680);
            Assert.Equal(Path.Combine(Env.WorkDir, "Files", "24680"), policy.DefaultWorkingDirectory, ignoreCase: true);
        } finally {
            Env.SandboxieGroupFilesRoot = originalGroupRoot;
        }
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("quote\"here", "\"quote\\\"here\"")]
    public void QuoteArgument_UsesWindowsCommandLineRules(string value, string expected) {
        Assert.Equal(expected, WindowsAppContainerNative.QuoteArgument(value));
    }

    [Fact]
    public async Task AppContainer_CanLoadTelegramSearchBotWithoutBotWorkDirectoryAccess() {
        if (!OperatingSystem.IsWindows()) return;

        var executable = Path.Combine(AppContext.BaseDirectory, "TelegramSearchBot.exe");
        if (!File.Exists(executable)) return;
        var profile = "TelegramSearchBot.Test." + Guid.NewGuid().ToString("N");
        WindowsAppContainerNative.AppContainerProcess? process = null;
        try {
            var sid = WindowsAppContainerNative.EnsureProfile(profile, profile);
            WindowsAppContainerNative.GrantReadOnlyDirectory(AppContext.BaseDirectory, sid);
            process = WindowsAppContainerNative.Start(
                sid,
                executable,
                ["SandboxToolHost"],
                AppContext.BaseDirectory,
                [],
                4,
                512L * 1024 * 1024);

            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.Process.WaitForExitAsync(timeout.Token);

            Assert.Equal(1, process.Process.ExitCode);
        } finally {
            process?.Dispose();
            try { WindowsAppContainerNative.DeleteProfile(profile); } catch { }
        }
    }

    [Fact]
    public async Task AppContainerAcl_AllowsAuthorizedDirectoryAndDeniesSibling() {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Env.WorkDir, "WindowsSandboxTests", Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "allowed");
        var denied = Path.Combine(root, "denied");
        var profile = "TelegramSearchBot.Test." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(denied);
        await File.WriteAllTextAsync(Path.Combine(denied, "secret.txt"), "SECRET");
        WindowsAppContainerNative.AppContainerProcess? process = null;
        try {
            var sid = WindowsAppContainerNative.EnsureProfile(profile, profile);
            WindowsAppContainerNative.GrantWritableDirectory(allowed, sid);
            process = WindowsAppContainerNative.Start(
                sid,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "echo OK>write.txt"],
                allowed,
                ["internetClient", "privateNetworkClientServer"],
                8,
                256L * 1024 * 1024);

            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.Process.WaitForExitAsync(timeout.Token);
            Assert.True(File.Exists(Path.Combine(allowed, "write.txt")), $"Authorized write failed; cmd exit code was {process.Process.ExitCode}.");
            Assert.Equal("OK", (await File.ReadAllTextAsync(Path.Combine(allowed, "write.txt"))).Trim());
            process.Dispose();
            process = null;

            process = WindowsAppContainerNative.Start(
                sid,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", $"type \"{Path.Combine(denied, "secret.txt")}\">secret-copy.txt"],
                allowed,
                [],
                8,
                256L * 1024 * 1024);
            await process.Process.WaitForExitAsync(timeout.Token);

            Assert.Equal(1, process.Process.ExitCode);
            Assert.True(File.Exists(Path.Combine(allowed, "secret-copy.txt")));
            Assert.Equal(0, new FileInfo(Path.Combine(allowed, "secret-copy.txt")).Length);
        } finally {
            process?.Dispose();
            try { WindowsAppContainerNative.RemoveDirectoryRules(root, WindowsAppContainerNative.EnsureProfile(profile, profile)); } catch { }
            try { WindowsAppContainerNative.DeleteProfile(profile); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
