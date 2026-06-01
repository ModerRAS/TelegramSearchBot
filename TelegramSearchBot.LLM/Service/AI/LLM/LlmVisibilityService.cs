using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class LlmVisibilityService : IService {
        private readonly DataDbContext _dbContext;

        public LlmVisibilityService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public string ServiceName => "LlmVisibilityService";

        public async Task<bool> IsUserInvisibleAsync(long groupId, long userId, CancellationToken cancellationToken = default) {
            if (groupId == 0 || userId == 0) return false;

            return await _dbContext.UsersWithGroup.AsNoTracking()
                .AnyAsync(x => x.GroupId == groupId &&
                               x.UserId == userId &&
                               x.IsLlmInvisible,
                    cancellationToken);
        }

        public async Task<HashSet<long>> GetInvisibleUserIdsAsync(long groupId, CancellationToken cancellationToken = default) {
            if (groupId == 0) return new HashSet<long>();

            var userIds = await _dbContext.UsersWithGroup.AsNoTracking()
                .Where(x => x.GroupId == groupId && x.IsLlmInvisible)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);

            return userIds.ToHashSet();
        }

        public async Task<List<Message>> FilterVisibleMessagesAsync(
            long groupId,
            IEnumerable<Message> messages,
            CancellationToken cancellationToken = default) {
            var messageList = messages?.ToList() ?? new List<Message>();
            if (messageList.Count == 0) return messageList;

            var invisibleUserIds = await GetInvisibleUserIdsAsync(groupId, cancellationToken);
            if (invisibleUserIds.Count == 0) return messageList;

            return messageList
                .Where(message => !invisibleUserIds.Contains(message.FromUserId))
                .ToList();
        }

        public async Task<bool> SetUserInvisibleAsync(
            long groupId,
            long userId,
            bool isInvisible,
            CancellationToken cancellationToken = default) {
            var userWithGroup = await _dbContext.UsersWithGroup
                .FirstOrDefaultAsync(x => x.GroupId == groupId && x.UserId == userId, cancellationToken);

            if (userWithGroup == null) {
                userWithGroup = new UserWithGroup {
                    GroupId = groupId,
                    UserId = userId,
                    IsLlmInvisible = isInvisible
                };
                await _dbContext.UsersWithGroup.AddAsync(userWithGroup, cancellationToken);
            } else {
                userWithGroup.IsLlmInvisible = isInvisible;
                _dbContext.UsersWithGroup.Update(userWithGroup);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return userWithGroup.IsLlmInvisible;
        }
    }
}
