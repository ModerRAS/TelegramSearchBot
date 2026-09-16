using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>加载结果：预设列表 + 数据版本 + 来源（便于日志与测试）。</summary>
    public sealed record LlmProviderCatalogDocument(
        DateTimeOffset? GeneratedAt,
        IReadOnlyList<LlmProviderPreset> Presets,
        string Source,
        string? Warning = null);

    /// <summary>
    /// providers.json 加载器：优先用户覆盖 `%LOCALAPPDATA%/TelegramSearchBot/providers.json`
    /// （不改代码、不发版即可增改服务商），其次程序集内置资源；
    /// 覆盖文件损坏/为空时回退内置并在 Warning 中说明，绝不因目录问题启动失败。
    /// </summary>
    public static class LlmProviderCatalogLoader {
        public const string OverrideFileName = "providers.json";
        public const string BuiltInResourceName = "TelegramSearchBot.Service.AI.LLM.providers.json";

        private static readonly JsonSerializerOptions SerializerOptions = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static LlmProviderCatalogDocument Load(string? overridePath = null, Stream? builtIn = null) {
            var path = overridePath ?? ResolveDefaultOverridePath();
            string? warning = null;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
                try {
                    return Parse(File.ReadAllText(path), $"file:{path}");
                } catch (Exception ex) {
                    warning = $"providers.json 覆盖文件解析失败，回退内置目录: {ex.Message}";
                }
            } else if (overridePath != null) {
                warning = $"providers.json 覆盖文件不存在，使用内置目录: {overridePath}";
            }

            if (builtIn == null) {
                using var stream = typeof(LlmProviderCatalogLoader).Assembly.GetManifestResourceStream(BuiltInResourceName)
                                   ?? throw new InvalidOperationException($"内置预设目录资源缺失: {BuiltInResourceName}");
                using var reader = new StreamReader(stream);
                return Parse(reader.ReadToEnd(), "builtin", warning);
            }

            builtIn.Position = 0;
            using (var reader = new StreamReader(builtIn, leaveOpen: true)) {
                return Parse(reader.ReadToEnd(), "builtin", warning);
            }
        }

        /// <summary>Env.WorkDir 需要 Config.json，测试/无配置环境下退回空路径（只用内置目录）。</summary>
        private static string ResolveDefaultOverridePath() {
            try {
                return Path.Combine(Env.WorkDir, OverrideFileName);
            } catch {
                return string.Empty;
            }
        }

        private static LlmProviderCatalogDocument Parse(string json, string source, string? warning = null) {
            var file = JsonSerializer.Deserialize<CatalogFile>(json, SerializerOptions)
                       ?? throw new InvalidOperationException("预设目录内容为空");
            if (file.Presets == null || file.Presets.Count == 0) {
                throw new InvalidOperationException("预设目录里没有任何 preset");
            }

            return new LlmProviderCatalogDocument(file.GeneratedAt, file.Presets, source, warning);
        }

        private sealed record CatalogFile(DateTimeOffset? GeneratedAt, List<LlmProviderPreset> Presets);
    }
}
