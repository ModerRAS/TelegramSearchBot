using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Tools {
    public sealed record CodingAgentWorkspaceValidationResult(bool IsValid, string FullPath, string ErrorMessage);

    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public sealed class CodingAgentPathPolicy : IService {
        private readonly ILogger<CodingAgentPathPolicy> _logger;

        public CodingAgentPathPolicy(ILogger<CodingAgentPathPolicy> logger) {
            _logger = logger;
        }

        public string ServiceName => nameof(CodingAgentPathPolicy);

        public CodingAgentWorkspaceValidationResult ValidateWorkspace(string workingDirectory) {
            if (string.IsNullOrWhiteSpace(workingDirectory)) {
                return new CodingAgentWorkspaceValidationResult(false, string.Empty, "workingDirectory is required.");
            }

            string fullPath;
            try {
                fullPath = NormalizePath(workingDirectory);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Invalid coding agent working directory: {WorkingDirectory}", workingDirectory);
                return new CodingAgentWorkspaceValidationResult(false, string.Empty, $"Invalid path: {ex.Message}");
            }

            if (!Directory.Exists(fullPath)) {
                return new CodingAgentWorkspaceValidationResult(false, fullPath, $"Directory does not exist: {fullPath}");
            }

            if (IsRootPath(fullPath)) {
                return new CodingAgentWorkspaceValidationResult(false, fullPath, "Refusing to run a coding agent at a filesystem root.");
            }

            foreach (var deniedPrefix in Env.CodingAgentDeniedPathPrefixes) {
                if (IsSameOrChildPath(fullPath, deniedPrefix)) {
                    return new CodingAgentWorkspaceValidationResult(
                        false,
                        fullPath,
                        $"Path is denied by CodingAgentDeniedPathPrefixes: {deniedPrefix}");
                }
            }

            return new CodingAgentWorkspaceValidationResult(true, fullPath, string.Empty);
        }

        private static string NormalizePath(string path) {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (expanded == "~") {
                expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            } else if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal)) {
                expanded = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    expanded[2..]);
            }

            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsRootPath(string fullPath) {
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(root) &&
                   string.Equals(
                       fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       GetPathComparison());
        }

        private static bool IsSameOrChildPath(string fullPath, string prefix) {
            if (string.IsNullOrWhiteSpace(prefix)) {
                return false;
            }

            string normalizedPrefix;
            try {
                normalizedPrefix = Path.GetFullPath(prefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            } catch {
                return false;
            }

            if (string.Equals(fullPath, normalizedPrefix, GetPathComparison())) {
                return true;
            }

            var prefixWithSeparator = normalizedPrefix + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefixWithSeparator, GetPathComparison())) {
                return true;
            }

            if (Path.AltDirectorySeparatorChar == Path.DirectorySeparatorChar) {
                return false;
            }

            var altPrefixWithSeparator = normalizedPrefix + Path.AltDirectorySeparatorChar;
            return fullPath.StartsWith(altPrefixWithSeparator, GetPathComparison());
        }

        private static StringComparison GetPathComparison() {
            return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }
    }
}
