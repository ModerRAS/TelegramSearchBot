using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

#nullable enable

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Scoped)]
    public class GroupLlmSettingsService : IService, IGroupLlmSettingsService {
        private const int MinBatchWindowSeconds = 1;
        private const int MaxBatchWindowSeconds = 60;
        private readonly DataDbContext _dbContext;

        public GroupLlmSettingsService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public string ServiceName => nameof(GroupLlmSettingsService);

        public async Task<string?> GetModelAsync(long chatId, CancellationToken cancellationToken = default) {
            var settings = await _dbContext.GroupSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
            return settings == null ? null : settings.LLMModelName;
        }

        public async Task<GroupAgentChatSettings> GetAgentChatSettingsAsync(long chatId, CancellationToken cancellationToken = default) {
            var settings = await _dbContext.GroupSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);

            if (settings is null) {
                return new GroupAgentChatSettings();
            }

            return new GroupAgentChatSettings {
                IsEnabled = settings.IsAgentChatEnabled,
                Mode = settings.AgentChatMode,
                BatchWindowSeconds = NormalizeBatchWindow(settings.AgentChatBatchWindowSeconds),
                ModelName = settings.LLMModelName
            };
        }

        public async Task<(string Previous, string Current)> SetModelAsync(long chatId, string modelName, CancellationToken cancellationToken = default) {
            var normalizedModelName = modelName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedModelName)) {
                throw new ArgumentException("Model name cannot be empty.", nameof(modelName));
            }

            modelName = normalizedModelName;
            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
            var previous = settings == null ? null : settings.LLMModelName;
            GroupSettings? newSettings = null;

            if (settings is null) {
                newSettings = new GroupSettings {
                    GroupId = chatId,
                    LLMModelName = modelName
                };
                await _dbContext.GroupSettings.AddAsync(newSettings, cancellationToken);
            } else {
                settings.LLMModelName = modelName;
            }

            try {
                await _dbContext.SaveChangesAsync(cancellationToken);
            } catch (DbUpdateException) when (newSettings != null) {
                _dbContext.Entry(newSettings).State = EntityState.Detached;
                settings = await _dbContext.GroupSettings
                    .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
                if (settings is null) {
                    throw;
                }

                previous = settings.LLMModelName;
                settings.LLMModelName = modelName;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return (previous ?? "Default", modelName);
        }

        public async Task<GroupAgentChatSettings> SetAgentChatModeAsync(
            long chatId,
            bool isEnabled,
            GroupAgentChatMode mode,
            int? batchWindowSeconds = null,
            CancellationToken cancellationToken = default) {
            var normalizedWindow = NormalizeBatchWindow(batchWindowSeconds);
            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
            GroupSettings? newSettings = null;

            if (settings is null) {
                newSettings = new GroupSettings {
                    GroupId = chatId
                };
                settings = newSettings;
                await _dbContext.GroupSettings.AddAsync(settings, cancellationToken);
            }

            settings.IsAgentChatEnabled = isEnabled;
            settings.AgentChatMode = mode;
            settings.AgentChatBatchWindowSeconds = normalizedWindow;

            try {
                await _dbContext.SaveChangesAsync(cancellationToken);
            } catch (DbUpdateException) when (newSettings != null) {
                _dbContext.Entry(newSettings).State = EntityState.Detached;
                settings = await _dbContext.GroupSettings
                    .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
                if (settings is null) {
                    throw;
                }

                settings.IsAgentChatEnabled = isEnabled;
                settings.AgentChatMode = mode;
                settings.AgentChatBatchWindowSeconds = normalizedWindow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new GroupAgentChatSettings {
                IsEnabled = settings.IsAgentChatEnabled,
                Mode = settings.AgentChatMode,
                BatchWindowSeconds = NormalizeBatchWindow(settings.AgentChatBatchWindowSeconds),
                ModelName = settings.LLMModelName
            };
        }

        private static int NormalizeBatchWindow(int? batchWindowSeconds) {
            if (!batchWindowSeconds.HasValue || batchWindowSeconds.Value <= 0) {
                return GroupAgentChatSettings.DefaultBatchWindowSeconds;
            }

            return Math.Clamp(batchWindowSeconds.Value, MinBatchWindowSeconds, MaxBatchWindowSeconds);
        }
    }
}
