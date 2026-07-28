using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Scoped)]
    public class PromptCachingSettingsService : IService {
        public const string PromptCachingEnabledKey = "LLM:PromptCachingEnabled";

        private readonly DataDbContext _dbContext;
        private readonly ILogger<PromptCachingSettingsService> _logger;

        public string ServiceName => "PromptCachingSettingsService";

        public PromptCachingSettingsService(
            DataDbContext dbContext,
            ILogger<PromptCachingSettingsService> logger) {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) {
            var value = await _dbContext.AppConfigurationItems
                .AsNoTracking()
                .Where(item => item.Key == PromptCachingEnabledKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(value)) {
                return true;
            }

            if (TryParseBoolean(value, out var enabled)) {
                return enabled;
            }

            _logger.LogWarning(
                "Invalid prompt caching setting value. Key={Key}, Value={Value}. Falling back to enabled=true.",
                PromptCachingEnabledKey,
                value);
            return true;
        }

        private static bool TryParseBoolean(string value, out bool enabled) {
            var trimmed = value.Trim();
            if (bool.TryParse(trimmed, out enabled)) {
                return true;
            }

            if (string.Equals(trimmed, "1", StringComparison.Ordinal)) {
                enabled = true;
                return true;
            }

            if (string.Equals(trimmed, "0", StringComparison.Ordinal)) {
                enabled = false;
                return true;
            }

            enabled = true;
            return false;
        }
    }
}
