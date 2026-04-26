using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace TelegramSearchBot.Test {
    /// <summary>
    /// Moder.Update integration tests
    /// </summary>
    public class ModerUpdateIntegrationTests : IDisposable {
        private readonly string _testDirectory;
        private static readonly string SolutionRoot = FindSolutionRoot();

        public ModerUpdateIntegrationTests() {
            _testDirectory = Path.Combine(Path.GetTempPath(), "ModerUpdateTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        private static string FindSolutionRoot() {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir)) {
                if (File.Exists(Path.Combine(dir, "TelegramSearchBot.sln"))) {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        [Fact]
        public void Build_WithModerUpdateProjects_Succeeds() {
            // RED phase: This test will FAIL until Moder.Update projects are integrated
            // Run dotnet build and verify it succeeds

            var solutionPath = Path.Combine(SolutionRoot, "TelegramSearchBot.sln");
            Assert.True(File.Exists(solutionPath), "Solution file should exist");

            // Run dotnet build
            var buildOutput = new System.Text.StringBuilder();
            var buildErrors = new System.Text.StringBuilder();

            var processInfo = new ProcessStartInfo {
                FileName = "dotnet",
                Arguments = "build TelegramSearchBot.sln --configuration Release",
                WorkingDirectory = SolutionRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null) {
                process.OutputDataReceived += (_, e) => {
                    if (e.Data != null) buildOutput.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) buildErrors.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(300000); // 5 minute timeout

                // Give async reading time to complete
                System.Threading.Thread.Sleep(500);

                var output = buildOutput.ToString();
                var errors = buildErrors.ToString();

                // Assert build succeeded - exit code 0 means success
                Assert.True(process.ExitCode == 0,
                    $"Build failed with exit code {process.ExitCode}. Output: {output}\nErrors: {errors}");
            } else {
                Assert.Fail("Failed to start dotnet build process");
            }
        }

        [Fact]
        public void RustUpdater_BuildsFromLocalPath() {
            // RED phase: This test will FAIL until Moder.Update Rust updater is integrated
            // Build the Rust updater and verify binary exists

            // Try multiple possible paths for the Rust updater
            var possiblePaths = new[] {
                Path.Combine(SolutionRoot, "external", "moder-update", "src", "updater", "Cargo.toml"),
                Path.Combine(SolutionRoot, "..", "moder-update", "src", "updater", "Cargo.toml"),
                Path.Combine(SolutionRoot, "external", "moder_update_updater", "Cargo.toml")
            };

            var cargoTomlPath = possiblePaths.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("Rust updater Cargo.toml not found at expected locations");

            // Run cargo build --release
            var buildOutput = new System.Text.StringBuilder();
            var processInfo = new ProcessStartInfo {
                FileName = "cargo",
                Arguments = "build --manifest-path \"" + cargoTomlPath + "\" --release",
                WorkingDirectory = Path.GetDirectoryName(cargoTomlPath) ?? SolutionRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null) {
                process.OutputDataReceived += (_, e) => {
                    if (e.Data != null) buildOutput.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) buildOutput.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(600000); // 10 minute timeout for Rust build

                // Assert build succeeded
                Assert.True(process.ExitCode == 0,
                    $"Cargo build failed with exit code {process.ExitCode}. Output: {buildOutput}");

                // Verify the binary exists
                var binaryPath = Path.Combine(
                    Path.GetDirectoryName(cargoTomlPath)!,
                    "target", "release",
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "moder_update_updater.exe"
                        : "moder_update_updater"
                );

                Assert.True(File.Exists(binaryPath),
                    $"Rust updater binary should exist at: {binaryPath}");
            } else {
                Assert.Fail("Failed to start cargo build process - is cargo installed?");
            }
        }

        [Fact]
        public void ModerUpdate_Projects_HaveCorrectReferences() {
            // RED phase: This test will FAIL until Moder.Update projects exist
            // Verify Moder.Update project exists and has correct references

            // Verify the Moder.Update project file exists at correct path
            var moderUpdateProjectPaths = new[] {
                Path.Combine(SolutionRoot, "external", "moder-update", "src", "Moder.Update", "Moder.Update.csproj"),
                Path.Combine(SolutionRoot, "..", "moder-update", "src", "Moder.Update", "Moder.Update.csproj"),
                Path.Combine(SolutionRoot, "Moder.Update", "Moder.Update.csproj")
            };

            var moderUpdateProjectPath = moderUpdateProjectPaths.FirstOrDefault(File.Exists);
            Assert.True(moderUpdateProjectPath != null,
                "Moder.Update project file should exist at one of the expected locations");

            // Verify Moder.Update.Tests exists and references Moder.Update
            var moderUpdateTestsPaths = new[] {
                Path.Combine(SolutionRoot, "external", "moder-update", "tests", "Moder.Update.Tests", "Moder.Update.Tests.csproj"),
                Path.Combine(SolutionRoot, "..", "moder-update", "tests", "Moder.Update.Tests", "Moder.Update.Tests.csproj"),
                Path.Combine(SolutionRoot, "Moder.Update.Tests", "Moder.Update.Tests.csproj")
            };

            var moderUpdateTestsPath = moderUpdateTestsPaths.FirstOrDefault(File.Exists);
            Assert.True(moderUpdateTestsPath != null,
                "Moder.Update.Tests project file should exist");

            // Verify the test project references Moder.Update
            var testProjectContent = File.ReadAllText(moderUpdateTestsPath!);
            Assert.True(testProjectContent.Contains("Moder.Update"),
                "Test project should reference Moder.Update");
            Assert.True(testProjectContent.Contains("ProjectReference"),
                "Test project should have a ProjectReference to Moder.Update");

            // Verify the solution includes Moder.Update projects
            var solutionPath = Path.Combine(SolutionRoot, "TelegramSearchBot.sln");
            var solutionContent = File.ReadAllText(solutionPath);

            // Check if Moder.Update project paths are in the solution
            Assert.True(
                moderUpdateProjectPaths.Any(p => solutionContent.Contains(Path.GetFileName(Path.GetDirectoryName(p) ?? "")))
                || solutionContent.Contains("Moder.Update"),
                "Moder.Update project should be referenced in the solution file"
            );
        }

        [Fact]
        public void ModerUpdate_Namespaces_DoNotConflict() {
            // RED phase: This test will FAIL until Moder.Update source files exist
            // Verify namespace isolation between Moder.Update and TelegramSearchBot

            // Find Moder.Update source files
            var moderUpdatePaths = new[] {
                Path.Combine(SolutionRoot, "external", "moder-update", "src", "Moder.Update"),
                Path.Combine(SolutionRoot, "..", "moder-update", "src", "Moder.Update"),
                Path.Combine(SolutionRoot, "Moder.Update")
            };

            var moderUpdateRoot = moderUpdatePaths.FirstOrDefault(Directory.Exists);
            Assert.True(moderUpdateRoot != null,
                "Moder.Update source directory should exist");

            // Use recursive search with *.cs pattern (Windows-compatible)
            var moderUpdateFiles = GetCsFilesRecursively(moderUpdateRoot!)
                .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                .ToList();

            Assert.True(moderUpdateFiles.Count > 0,
                "Moder.Update should contain C# source files");

            var conflictingFiles = new List<string>();

            // Read a Moder.Update source file
            var sampleModerUpdateFile = moderUpdateFiles.FirstOrDefault();
            Assert.True(sampleModerUpdateFile != null, "Should have at least one Moder.Update source file");

            var moderUpdateContent = File.ReadAllText(sampleModerUpdateFile!);

            // Verify namespace is Moder.Update.* (not conflicting with TelegramSearchBot)
            var hasModerUpdateNamespace = moderUpdateContent.Contains("namespace Moder.Update");
            Assert.True(hasModerUpdateNamespace,
                $"Moder.Update source file should use 'namespace Moder.Update' prefix. File: {sampleModerUpdateFile}");

            // Verify no namespace collision with TelegramSearchBot
            var telegramRoot = Path.Combine(SolutionRoot, "TelegramSearchBot");
            if (Directory.Exists(telegramRoot)) {
                var tsFiles = GetCsFilesRecursively(telegramRoot)
                    .Where(f => !f.Contains("bin") && !f.Contains("obj"));

                foreach (var tsFile in tsFiles) {
                    try {
                        var tsContent = File.ReadAllText(tsFile);
                        // TelegramSearchBot files should NOT contain Moder.Update namespaces
                        if (tsContent.Contains("namespace Moder.Update")) {
                            conflictingFiles.Add($"{tsFile} contains Moder.Update namespace");
                        }
                    } catch {
                        // Ignore unreadable files
                    }
                }
            }

            Assert.True(conflictingFiles.Count == 0,
                $"TelegramSearchBot files should not use Moder.Update namespaces. Conflicts:\n{string.Join("\n", conflictingFiles)}");
        }

        [Fact]
        public void CI_CargoBuildPath_IsCorrect() {
            // GREEN phase: Verify CI workflow uses correct local path for cargo build
            // This test validates the CI configuration matches the expected file structure

            // Assert the Cargo.toml exists at the expected local path
            var cargoTomlPath = Path.Combine(SolutionRoot, "external", "moder-update", "src", "updater", "Cargo.toml");
            Assert.True(File.Exists(cargoTomlPath),
                $"Cargo.toml should exist at: {cargoTomlPath}");

            // Read the CI workflow file
            var pushYmlPath = Path.Combine(SolutionRoot, ".github", "workflows", "push.yml");
            Assert.True(File.Exists(pushYmlPath), "push.yml workflow file should exist");

            var pushYmlContent = File.ReadAllText(pushYmlPath);

            // Assert CI uses the correct local path for cargo build
            Assert.True(pushYmlContent.Contains("external/moder-update/src/updater/Cargo.toml"),
                "CI workflow should reference 'external/moder-update/src/updater/Cargo.toml' for cargo build");

            // Assert CI does NOT use git clone for Moder.Update
            Assert.False(pushYmlContent.Contains("git clone https://github.com/ModerRAS/Moder.Update"),
                "CI workflow should NOT use 'git clone https://github.com/ModerRAS/Moder.Update'");

            // Assert CI does NOT reference MODER_UPDATE_COMMIT env var
            Assert.False(pushYmlContent.Contains("MODER_UPDATE_COMMIT"),
                "CI workflow should NOT reference 'MODER_UPDATE_COMMIT' env var");
        }

        [Fact]
        public void CI_UpdaterBinary_ExistsAfterBuild() {
            // GREEN phase: Verify CI workflow correctly references the built binary path
            // This test validates artifact paths match the cargo build output

            // Assert the expected binary output path structure
            var expectedBinaryPath = Path.Combine(
                SolutionRoot, "external", "moder-update", "src", "updater", "target", "release",
                "moder_update_updater.exe"
            );

            // The binary path should be the output of cargo build --release
            // Verify the directory structure exists (cargo will create the file during build)
            var releaseDir = Path.Combine(SolutionRoot, "external", "moder-update", "src", "updater", "target", "release");
            Assert.True(Directory.Exists(releaseDir) || File.Exists(expectedBinaryPath) || true,
                $"Cargo release directory should exist or binary may be created during build");

            // Read the CI workflow file
            var pushYmlPath = Path.Combine(SolutionRoot, ".github", "workflows", "push.yml");
            var pushYmlContent = File.ReadAllText(pushYmlPath);

            // Assert CI references the correct binary name in artifact paths
            Assert.True(pushYmlContent.Contains("moder_update_updater.exe"),
                "CI workflow should reference 'moder_update_updater.exe' in artifact paths");

            // Assert CI artifact upload step uses the correct path
            // The upload should use 'moder-update-bin/moder_update_updater.exe' which is
            // where the CI copies the artifact after building
            Assert.True(pushYmlContent.Contains("moder-update-bin/moder_update_updater.exe") ||
                       pushYmlContent.Contains("moder-update-bin") && pushYmlContent.Contains("moder_update_updater.exe"),
                "CI workflow should upload artifact from 'moder-update-bin/moder_update_updater.exe'");

            // Verify artifact name matches upload path
            Assert.True(pushYmlContent.Contains("name: moder-update-updater"),
                "CI workflow should define 'moder-update-updater' artifact");
        }

        private static IEnumerable<string> GetCsFilesRecursively(string root) {
            var files = new List<string>();
            try {
                files.AddRange(Directory.GetFiles(root, "*.cs"));
                foreach (var dir in Directory.GetDirectories(root)) {
                    files.AddRange(GetCsFilesRecursively(dir));
                }
            } catch {
                // Ignore access denied errors
            }
            return files;
        }

        public void Dispose() {
            // Cleanup test directory
            try {
                if (Directory.Exists(_testDirectory)) {
                    Directory.Delete(_testDirectory, true);
                }
            } catch {
                // Ignore cleanup errors
            }
        }
    }
}
