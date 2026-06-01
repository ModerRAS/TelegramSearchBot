using System;
using System.IO;
using System.Linq;
using TelegramSearchBot.Common;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    [Collection("AgentEnvSerial")]
    public class SandboxieToolHostServiceTests {
        [Fact]
        public void BuildPortableBoxIni_AllowsOnlyCurrentChatResourceDirectories() {
            var originalGroupFilesRoot = Env.SandboxieGroupFilesRoot;
            var originalDenyHostFileSystem = Env.SandboxieDenyHostFileSystem;
            Env.SandboxieGroupFilesRoot = string.Empty;
            Env.SandboxieDenyHostFileSystem = false;

            try {
                var chatId = 12345L;
                Directory.CreateDirectory(Path.Combine(Env.WorkDir, "logs"));
                var ini = BuildIni(chatId);

                Assert.Contains(ReadPath(Path.Combine(Env.WorkDir, "Photos", chatId.ToString())), ini);
                Assert.Contains(ReadPath(Path.Combine(Env.WorkDir, "Audios", chatId.ToString())), ini);
                Assert.Contains(ReadPath(Path.Combine(Env.WorkDir, "Videos", chatId.ToString())), ini);
                Assert.Contains(ReadPath(Path.Combine(Env.WorkDir, "Files", chatId.ToString())), ini);

                Assert.Contains($"ClosedFilePath={Normalize(Path.Combine(Env.WorkDir, "Photos"))}", ini);
                Assert.Contains($"ClosedFilePath={Normalize(Path.Combine(Env.WorkDir, "Audios"))}", ini);
                Assert.Contains($"ClosedFilePath={Normalize(Path.Combine(Env.WorkDir, "Videos"))}", ini);
                Assert.Contains($"ClosedFilePath={Normalize(Path.Combine(Env.WorkDir, "Files"))}", ini);
                Assert.Contains($"ClosedFilePath={Normalize(Path.Combine(Env.WorkDir, "logs"))}", ini);

                Assert.DoesNotContain("Index_Data", ini, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("GroupFiles", ini, StringComparison.OrdinalIgnoreCase);
            } finally {
                Env.SandboxieGroupFilesRoot = originalGroupFilesRoot;
                Env.SandboxieDenyHostFileSystem = originalDenyHostFileSystem;
            }
        }

        [Fact]
        public void BuildPortableBoxIni_WhenGroupFilesRootConfigured_AllowsOnlyCurrentChatSubdirectory() {
            var originalGroupFilesRoot = Env.SandboxieGroupFilesRoot;
            var originalDenyHostFileSystem = Env.SandboxieDenyHostFileSystem;
            Env.SandboxieGroupFilesRoot = Path.Combine(Env.WorkDir, "CustomGroupFiles");
            Env.SandboxieDenyHostFileSystem = false;

            try {
                var chatId = 67890L;
                var ini = BuildIni(chatId);

                Assert.Contains(ReadPath(Path.Combine(Env.SandboxieGroupFilesRoot, chatId.ToString())), ini);
                Assert.Contains(ClosedPath(Env.SandboxieGroupFilesRoot), ini);
                Assert.DoesNotContain(ReadPath(Path.Combine(Env.SandboxieGroupFilesRoot, "111")), ini);
            } finally {
                Env.SandboxieGroupFilesRoot = originalGroupFilesRoot;
                Env.SandboxieDenyHostFileSystem = originalDenyHostFileSystem;
            }
        }

        [Fact]
        public void BuildPortableBoxIni_WhenDenyHostFileSystemDisabled_DoesNotCloseDriveRoots() {
            var originalDenyHostFileSystem = Env.SandboxieDenyHostFileSystem;
            Env.SandboxieDenyHostFileSystem = false;

            try {
                var ini = BuildIni(13579L);
                foreach (var root in DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName)) {
                    Assert.DoesNotContain(ClosedPath(root), ini);
                }
            } finally {
                Env.SandboxieDenyHostFileSystem = originalDenyHostFileSystem;
            }
        }

        [Fact]
        public void EnsureBoxesDirectory_CreatesMissingDirectory() {
            var testDir = Path.Combine(Path.GetTempPath(), "TGSB_SandboxieBoxes_" + Guid.NewGuid().ToString("N"));
            try {
                Assert.False(Directory.Exists(testDir));

                SandboxieToolHostService.EnsureBoxesDirectory(testDir);

                Assert.True(Directory.Exists(testDir));
            } finally {
                if (Directory.Exists(testDir)) {
                    Directory.Delete(testDir, recursive: true);
                }
            }
        }

        private static string BuildIni(long chatId) {
            var instance = new SandboxieInstance(
                chatId,
                "TGSB_TEST",
                Path.Combine(Env.WorkDir, "Sandboxie", "Boxes"),
                Path.Combine(Env.WorkDir, "Sandboxie", "Boxes", "TGSB_TEST.ini"),
                Path.Combine(Env.WorkDir, "Sandboxie", "Boxes", "TGSB_TEST"));
            return SandboxieToolHostService.BuildPortableBoxIni(instance);
        }

        private static string ReadPath(string path) => $"ReadFilePath={Normalize(path)}\\*";
        private static string ClosedPath(string path) => $"ClosedFilePath={Normalize(path)}{(Directory.Exists(path) ? "\\*" : string.Empty)}";

        private static string Normalize(string path) => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
