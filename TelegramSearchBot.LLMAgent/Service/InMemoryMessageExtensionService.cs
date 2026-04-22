using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class InMemoryMessageExtensionService : IMessageExtensionService {
        private readonly DataDbContext _dbContext;

        public InMemoryMessageExtensionService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public string ServiceName => nameof(InMemoryMessageExtensionService);

        public Task<MessageExtension?> GetByIdAsync(int id) => _dbContext.MessageExtensions.FindAsync(id).AsTask();

        public Task<List<MessageExtension>> GetByMessageDataIdAsync(long messageDataId) {
            return _dbContext.MessageExtensions.Where(x => x.MessageDataId == messageDataId).ToListAsync();
        }

        public async Task AddOrUpdateAsync(MessageExtension extension) {
            var existing = await _dbContext.MessageExtensions.FirstOrDefaultAsync(x =>
                x.MessageDataId == extension.MessageDataId &&
                x.Name == extension.Name);

            if (existing == null) {
                await _dbContext.MessageExtensions.AddAsync(extension);
            } else {
                existing.Value = extension.Value;
            }

            await _dbContext.SaveChangesAsync();
        }

        public Task AddOrUpdateAsync(long messageDataId, string name, string value) {
            return AddOrUpdateAsync(new MessageExtension {
                MessageDataId = messageDataId,
                Name = name,
                Value = value
            });
        }

        public async Task DeleteAsync(int id) {
            var entity = await _dbContext.MessageExtensions.FindAsync(id);
            if (entity != null) {
                _dbContext.MessageExtensions.Remove(entity);
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task DeleteByMessageDataIdAsync(long messageDataId) {
            var items = await _dbContext.MessageExtensions.Where(x => x.MessageDataId == messageDataId).ToListAsync();
            _dbContext.MessageExtensions.RemoveRange(items);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<long?> GetMessageIdByMessageIdAndGroupId(long messageId, long groupId) {
            var entity = await _dbContext.Messages.FirstOrDefaultAsync(x => x.MessageId == messageId && x.GroupId == groupId);
            return entity?.Id;
        }
    }
}
