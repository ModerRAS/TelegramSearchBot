using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.Tools {
    /// <summary>
    /// Built-in tool for file operations: read, write, edit, search, and list files.
    /// Provides Claude Code-like text editing capabilities for the LLM.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class FileToolService : IService, IFileToolService {
        private readonly ILogger<FileToolService> _logger;
        private const int MaxFileSize = 1024 * 1024; // 1MB limit for reading
        private const int MaxOutputLength = 50000;

        public string ServiceName => "FileToolService";

        public FileToolService(ILogger<FileToolService> logger) {
            _logger = logger;
        }

        [BuiltInTool("Read the contents of a file. Supports reading specific line ranges. Returns file content with line numbers.")]
        public async Task<string> ReadFile(
            [BuiltInParameter("Absolute or relative path to the file to read")] string path,
            ToolContext toolContext,
            [BuiltInParameter("Starting line number (1-based). If omitted, reads from the beginning.", IsRequired = false)] int? startLine = null,
            [BuiltInParameter("Ending line number (1-based, inclusive). If omitted, reads to the end.", IsRequired = false)] int? endLine = null) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: File operations are only available to admin users.";
            }

            try {
                path = ResolvePath(path);

                if (!File.Exists(path)) {
                    return $"Error: File not found: {path}";
                }

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > MaxFileSize) {
                    return $"Error: File is too large ({fileInfo.Length} bytes). Maximum supported size is {MaxFileSize} bytes. Use startLine/endLine parameters to read a portion.";
                }

                var lines = await File.ReadAllLinesAsync(path);
                var sb = new StringBuilder();

                int start = Math.Max(1, startLine ?? 1);
                int end = Math.Min(lines.Length, endLine ?? lines.Length);

                if (start > lines.Length) {
                    return $"Error: Start line {start} is beyond the file length ({lines.Length} lines).";
                }

                sb.AppendLine($"File: {path} ({lines.Length} lines total, showing lines {start}-{end})");
                sb.AppendLine("---");

                for (int i = start - 1; i < end && i < lines.Length; i++) {
                    sb.AppendLine($"{i + 1}. {lines[i]}");
                }

                var result = sb.ToString();
                if (result.Length > MaxOutputLength) {
                    result = result[..MaxOutputLength] + "\n... [output truncated]";
                }
                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error reading file: {Path}", path);
                return $"Error reading file: {ex.Message}";
            }
        }

        [BuiltInTool("Write content to a file. Creates the file if it doesn't exist, creates parent directories as needed.")]
        public async Task<string> WriteFile(
            [BuiltInParameter("Absolute or relative path to the file to write")] string path,
            [BuiltInParameter("Content to write to the file")] string content,
            ToolContext toolContext) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: File operations are only available to admin users.";
            }

            try {
                path = ResolvePath(path);

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(path, content);
                var lineCount = content.Split('\n').Length;
                return $"Successfully wrote {content.Length} characters ({lineCount} lines) to {path}";
            } catch (Exception ex) {
                _logger.LogError(ex, "Error writing file: {Path}", path);
                return $"Error writing file: {ex.Message}";
            }
        }

        [BuiltInTool("Edit a file by replacing a specific text string with new text. The old text must match exactly (including whitespace and line breaks).")]
        public async Task<string> EditFile(
            [BuiltInParameter("Absolute or relative path to the file to edit")] string path,
            [BuiltInParameter("The exact text to find and replace. Must match the file content exactly.")] string oldText,
            [BuiltInParameter("The new text to replace the old text with")] string newText,
            ToolContext toolContext) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: File operations are only available to admin users.";
            }

            try {
                path = ResolvePath(path);

                if (!File.Exists(path)) {
                    return $"Error: File not found: {path}";
                }

                var content = await File.ReadAllTextAsync(path);

                // Normalize line endings for comparison
                var normalizedContent = content.Replace("\r\n", "\n");
                var normalizedOldText = oldText.Replace("\r\n", "\n");
                var normalizedNewText = newText.Replace("\r\n", "\n");

                var count = CountOccurrences(normalizedContent, normalizedOldText);
                if (count == 0) {
                    return $"Error: The specified old text was not found in {path}. Make sure the text matches exactly.";
                }
                if (count > 1) {
                    return $"Error: The specified old text was found {count} times in {path}. It must be unique. Include more context to make it unique.";
                }

                var newContent = normalizedContent.Replace(normalizedOldText, normalizedNewText);

                // Preserve original line ending style
                if (content.Contains("\r\n")) {
                    newContent = newContent.Replace("\n", "\r\n");
                }

                await File.WriteAllTextAsync(path, newContent);
                return $"Successfully edited {path}. Replaced 1 occurrence.";
            } catch (Exception ex) {
                _logger.LogError(ex, "Error editing file: {Path}", path);
                return $"Error editing file: {ex.Message}";
            }
        }

        [BuiltInTool("Search for a text pattern in files using regex. Returns matching lines with file paths and line numbers.")]
        public async Task<string> SearchText(
            [BuiltInParameter("Regex pattern to search for")] string pattern,
            ToolContext toolContext,
            [BuiltInParameter("Directory to search in. Defaults to bot work directory.", IsRequired = false)] string path = null,
            [BuiltInParameter("File glob pattern to filter files (e.g., '*.cs', '*.json'). Defaults to all files.", IsRequired = false)] string fileGlob = null,
            [BuiltInParameter("Whether to ignore case. Defaults to true.", IsRequired = false)] bool ignoreCase = true) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: File operations are only available to admin users.";
            }

            try {
                path = ResolvePath(path ?? Env.WorkDir);

                if (!Directory.Exists(path)) {
                    return $"Error: Directory not found: {path}";
                }

                var regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                var regex = new Regex(pattern, regexOptions, TimeSpan.FromSeconds(5));
                var searchPattern = fileGlob ?? "*";
                var sb = new StringBuilder();
                int matchCount = 0;
                int fileCount = 0;

                var files = Directory.EnumerateFiles(path, searchPattern, System.IO.SearchOption.AllDirectories)
                    .Take(1000); // Limit file count

                foreach (var file in files) {
                    try {
                        var lines = await File.ReadAllLinesAsync(file);
                        bool fileHasMatch = false;

                        for (int i = 0; i < lines.Length; i++) {
                            if (regex.IsMatch(lines[i])) {
                                if (!fileHasMatch) {
                                    sb.AppendLine($"\n{file}:");
                                    fileHasMatch = true;
                                    fileCount++;
                                }

                                sb.AppendLine($"  {i + 1}: {lines[i].TrimEnd()}");
                                matchCount++;

                                if (matchCount >= 100) {
                                    sb.AppendLine($"\n... [stopped at 100 matches, found in {fileCount} files]");
                                    return sb.ToString();
                                }
                            }
                        }
                    } catch {
                        // Skip binary or unreadable files
                    }

                    if (sb.Length > MaxOutputLength) {
                        sb.AppendLine("\n... [output truncated]");
                        break;
                    }
                }

                if (matchCount == 0) {
                    return $"No matches found for pattern '{pattern}' in {path}";
                }

                sb.Insert(0, $"Found {matchCount} matches in {fileCount} files:\n");
                return sb.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error searching files in: {Path}", path);
                return $"Error searching files: {ex.Message}";
            }
        }

        [BuiltInTool("List files and directories at a given path. Supports glob patterns.")]
        public async Task<string> ListFiles(
            ToolContext toolContext,
            [BuiltInParameter("Directory path to list. Defaults to bot work directory.", IsRequired = false)] string path = null,
            [BuiltInParameter("Glob pattern to filter files (e.g., '*.cs'). If omitted, lists all.", IsRequired = false)] string pattern = null) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: File operations are only available to admin users.";
            }

            try {
                path = ResolvePath(path ?? Env.WorkDir);

                if (!Directory.Exists(path)) {
                    return $"Error: Directory not found: {path}";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Contents of: {path}");
                sb.AppendLine("---");

                // List directories first
                var dirs = Directory.GetDirectories(path).OrderBy(d => d).Take(200);
                foreach (var dir in dirs) {
                    sb.AppendLine($"  [DIR] {Path.GetFileName(dir)}/");
                }

                // List files
                var searchPattern = pattern ?? "*";
                var files = Directory.GetFiles(path, searchPattern).OrderBy(f => f).Take(500);
                foreach (var file in files) {
                    var info = new FileInfo(file);
                    sb.AppendLine($"  {Path.GetFileName(file)} ({FormatFileSize(info.Length)})");
                }

                return await Task.FromResult(sb.ToString());
            } catch (Exception ex) {
                _logger.LogError(ex, "Error listing files in: {Path}", path);
                return $"Error listing files: {ex.Message}";
            }
        }

        private static string ResolvePath(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return Env.WorkDir;
            }
            if (!Path.IsPathRooted(path)) {
                return Path.GetFullPath(Path.Combine(Env.WorkDir, path));
            }
            return Path.GetFullPath(path);
        }

        private static int CountOccurrences(string text, string pattern) {
            int count = 0;
            int index = 0;
            while (( index = text.IndexOf(pattern, index, StringComparison.Ordinal) ) != -1) {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        private static string FormatFileSize(long bytes) {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / ( 1024.0 * 1024 ):F1}MB";
            return $"{bytes / ( 1024.0 * 1024 * 1024 ):F1}GB";
        }
    }
}
