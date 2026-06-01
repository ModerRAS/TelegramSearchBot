#pragma warning disable CS8602
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class FileToolServiceTests : IDisposable {
        private readonly Mock<ILogger<FileToolService>> _loggerMock;
        private readonly FileToolService _service;
        private readonly string _testDir;
        private readonly ToolContext _adminContext;
        private readonly ToolContext _nonAdminContext;

        public FileToolServiceTests() {
            _loggerMock = new Mock<ILogger<FileToolService>>();
            _service = new FileToolService(_loggerMock.Object);
            _testDir = Path.Combine(Path.GetTempPath(), $"FileToolServiceTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);

            _adminContext = new ToolContext { ChatId = 1, UserId = Env.AdminId };
            _nonAdminContext = new ToolContext { ChatId = 1, UserId = long.MaxValue - 1 };
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_testDir)) {
                    Directory.Delete(_testDir, true);
                }
            } catch { }
        }

        [Fact]
        public async Task ReadFile_NonAdmin_ReturnsError() {
            var result = await _service.ReadFile("test.txt", _nonAdminContext);
            Assert.Contains("Error", result);
            Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadFile_FileNotFound_ReturnsError() {
            var result = await _service.ReadFile("nonexistent.txt", _adminContext);
            Assert.Contains("Error", result);
            Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadFile_ReadsFileContent() {
            var filePath = Path.Combine(_testDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "line1\nline2\nline3");

            var result = await _service.ReadFile(filePath, _adminContext);
            Assert.Contains("line1", result);
            Assert.Contains("line2", result);
            Assert.Contains("line3", result);
            Assert.Contains("3 lines total", result);
        }

        [Fact]
        public async Task ReadFile_WithLineRange_ReadsPartialContent() {
            var filePath = Path.Combine(_testDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "line1\nline2\nline3\nline4\nline5");

            var result = await _service.ReadFile(filePath, _adminContext, startLine: 2, endLine: 4);
            Assert.Contains("line2", result);
            Assert.Contains("line3", result);
            Assert.Contains("line4", result);
            Assert.DoesNotContain("1. line1", result);
        }

        [Fact]
        public async Task WriteFile_CreatesNewFile() {
            var filePath = Path.Combine(_testDir, "new.txt");
            var result = await _service.WriteFile(filePath, "hello world", _adminContext);
            Assert.Contains("Successfully", result);
            Assert.True(File.Exists(filePath));
            Assert.Equal("hello world", await File.ReadAllTextAsync(filePath));
        }

        [Fact]
        public async Task WriteFile_NonAdmin_ReturnsError() {
            var result = await _service.WriteFile("test.txt", "content", _nonAdminContext);
            Assert.Contains("Error", result);
        }

        [Fact]
        public async Task WriteFile_CreatesParentDirectories() {
            var filePath = Path.Combine(_testDir, "sub", "dir", "new.txt");
            var result = await _service.WriteFile(filePath, "nested content", _adminContext);
            Assert.Contains("Successfully", result);
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task EditFile_ReplacesText() {
            var filePath = Path.Combine(_testDir, "edit.txt");
            await File.WriteAllTextAsync(filePath, "hello world\nfoo bar");

            var result = await _service.EditFile(filePath, "foo bar", "baz qux", _adminContext);
            Assert.Contains("Successfully", result);

            var content = await File.ReadAllTextAsync(filePath);
            Assert.Contains("baz qux", content);
            Assert.DoesNotContain("foo bar", content);
        }

        [Fact]
        public async Task EditFile_TextNotFound_ReturnsError() {
            var filePath = Path.Combine(_testDir, "edit.txt");
            await File.WriteAllTextAsync(filePath, "hello world");

            var result = await _service.EditFile(filePath, "nonexistent text", "replacement", _adminContext);
            Assert.Contains("Error", result);
            Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task EditFile_DuplicateText_ReturnsError() {
            var filePath = Path.Combine(_testDir, "edit.txt");
            await File.WriteAllTextAsync(filePath, "hello hello");

            var result = await _service.EditFile(filePath, "hello", "world", _adminContext);
            Assert.Contains("Error", result);
            Assert.Contains("2 times", result);
        }

        [Fact]
        public async Task SearchText_FindsMatches() {
            var subDir = Path.Combine(_testDir, "search");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "a.txt"), "hello world\nfoo bar");
            await File.WriteAllTextAsync(Path.Combine(subDir, "b.txt"), "hello again\nbaz");

            var result = await _service.SearchText("hello", _adminContext, path: subDir);
            Assert.Contains("hello", result);
            Assert.Contains("2 matches", result);
        }

        [Fact]
        public async Task SearchText_NoMatches_ReturnsNoResults() {
            var subDir = Path.Combine(_testDir, "search_empty");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "a.txt"), "hello world");

            var result = await _service.SearchText("zzzznotfound", _adminContext, path: subDir);
            Assert.Contains("No matches", result);
        }

        [Fact]
        public async Task ListFiles_ListsContents() {
            var subDir = Path.Combine(_testDir, "list");
            var innerDir = Path.Combine(subDir, "inner");
            Directory.CreateDirectory(innerDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "file1.txt"), "content");
            await File.WriteAllTextAsync(Path.Combine(subDir, "file2.cs"), "content");

            var result = await _service.ListFiles(_adminContext, path: subDir);
            Assert.Contains("inner", result);
            Assert.Contains("file1.txt", result);
            Assert.Contains("file2.cs", result);
        }

        [Fact]
        public async Task ListFiles_NonAdmin_ReturnsError() {
            var result = await _service.ListFiles(_nonAdminContext);
            Assert.Contains("Error", result);
        }
    }
}
