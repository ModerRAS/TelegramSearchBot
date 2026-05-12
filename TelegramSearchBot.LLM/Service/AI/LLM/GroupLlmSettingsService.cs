using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class GroupLlmSettingsService : IService, IGroupLlmSettingsService {
        private readonly DataDbContext _dbContext;

        public GroupLlmSettingsService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public string ServiceName => nameof(GroupLlmSettingsService);

        public async Task<string> GetModelAsync(long chatId, CancellationToken cancellationToken = default) {
            var settings = await _dbContext.GroupSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
            return settings == null ? null : settings.LLMModelName;
        }

        public async Task<(string Previous, string Current)> SetModelAsync(long chatId, string modelName, CancellationToken cancellationToken = default) {
            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(s => s.GroupId == chatId, cancellationToken);
            var previous = settings == null ? null : settings.LLMModelName;

            if (settings is null) {
                await _dbContext.GroupSettings.AddAsync(new GroupSettings {
                    GroupId = chatId,
                    LLMModelName = modelName
                }, cancellationToken);
            } else {
                settings.LLMModelName = modelName;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return (previous ?? "Default", modelName);
        }
    }
}
