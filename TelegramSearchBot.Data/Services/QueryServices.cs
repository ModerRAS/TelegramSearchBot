using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Data.Interfaces;

namespace TelegramSearchBot.Data.Services
{
    /// <summary>
    /// 消息查询服务实现
    /// </summary>
    public class MessageQueryService : IMessageQueryService
    {
        private readonly DataDbContext _context;

        public MessageQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Message?> GetByIdAsync(long id)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<Message?> GetByGroupAndMessageIdAsync(long groupId, long messageId)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .FirstOrDefaultAsync(m => m.GroupId == groupId && m.MessageId == messageId);
        }

        public async Task<List<Message>> GetByGroupIdAsync(long groupId, int skip = 0, int take = 50)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.GroupId == groupId)
                .OrderByDescending(m => m.DateTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<Message>> GetByUserIdAsync(long userId, int skip = 0, int take = 50)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.FromUserId == userId)
                .OrderByDescending(m => m.DateTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<Message>> SearchContentAsync(long groupId, string searchTerm, int skip = 0, int take = 50)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.GroupId == groupId && m.Content.Contains(searchTerm))
                .OrderByDescending(m => m.DateTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<Message>> GetByTimeRangeAsync(long groupId, DateTime startTime, DateTime endTime, int skip = 0, int take = 50)
        {
            return await _context.Messages
                .Include(m => m.MessageExtensions)
                .Where(m => m.GroupId == groupId && m.DateTime >= startTime && m.DateTime <= endTime)
                .OrderBy(m => m.DateTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<Message> AddAsync(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            return message;
        }

        public async Task<List<Message>> AddRangeAsync(List<Message> messages)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            await _context.Messages.AddRangeAsync(messages);
            await _context.SaveChangesAsync();
            return messages;
        }

        public async Task<Message> UpdateAsync(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            _context.Messages.Update(message);
            await _context.SaveChangesAsync();
            return message;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var message = await _context.Messages.FindAsync(id);
            if (message == null)
                return false;

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetCountAsync(long groupId)
        {
            return await _context.Messages
                .CountAsync(m => m.GroupId == groupId);
        }
    }

    /// <summary>
    /// 用户查询服务实现
    /// </summary>
    public class UserQueryService : IUserQueryService
    {
        private readonly DataDbContext _context;

        public UserQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<UserData?> GetByIdAsync(long id)
        {
            return await _context.UserData.FindAsync(id);
        }

        public async Task<UserData?> GetByUserNameAsync(string userName)
        {
            return await _context.UserData
                .FirstOrDefaultAsync(u => u.UserName == userName);
        }

        public async Task<List<UserData>> GetAllAsync(int skip = 0, int take = 50)
        {
            return await _context.UserData
                .OrderBy(u => u.UserName)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<UserData>> SearchAsync(string searchTerm, int skip = 0, int take = 50)
        {
            return await _context.UserData
                .Where(u => u.FirstName.Contains(searchTerm) || 
                           u.LastName.Contains(searchTerm) || 
                           u.UserName.Contains(searchTerm))
                .OrderBy(u => u.UserName)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<UserData> AddAsync(UserData user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            await _context.UserData.AddAsync(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<UserData> UpdateAsync(UserData user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            _context.UserData.Update(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var user = await _context.UserData.FindAsync(id);
            if (user == null)
                return false;

            _context.UserData.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }
    }

    /// <summary>
    /// 群组查询服务实现
    /// </summary>
    public class GroupQueryService : IGroupQueryService
    {
        private readonly DataDbContext _context;

        public GroupQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<GroupData?> GetByIdAsync(long id)
        {
            return await _context.GroupData.FindAsync(id);
        }

        public async Task<List<GroupData>> GetAllAsync(int skip = 0, int take = 50)
        {
            return await _context.GroupData
                .OrderBy(g => g.Title)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<GroupData>> SearchAsync(string searchTerm, int skip = 0, int take = 50)
        {
            return await _context.GroupData
                .Where(g => g.Title.Contains(searchTerm))
                .OrderBy(g => g.Title)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<GroupData>> GetNonBlacklistGroupsAsync(int skip = 0, int take = 50)
        {
            return await _context.GroupData
                .Where(g => !g.IsBlacklist)
                .OrderBy(g => g.Title)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<GroupData> AddAsync(GroupData group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            await _context.GroupData.AddAsync(group);
            await _context.SaveChangesAsync();
            return group;
        }

        public async Task<GroupData> UpdateAsync(GroupData group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            _context.GroupData.Update(group);
            await _context.SaveChangesAsync();
            return group;
        }

        public async Task<bool> SetBlacklistStatusAsync(long groupId, bool isBlacklist)
        {
            var group = await _context.GroupData.FindAsync(groupId);
            if (group == null)
                return false;

            group.IsBlacklist = isBlacklist;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var group = await _context.GroupData.FindAsync(id);
            if (group == null)
                return false;

            _context.GroupData.Remove(group);
            await _context.SaveChangesAsync();
            return true;
        }
    }

    /// <summary>
    /// LLM通道查询服务实现
    /// </summary>
    public class LLMChannelQueryService : ILLMChannelQueryService
    {
        private readonly DataDbContext _context;

        public LLMChannelQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<LLMChannel?> GetByIdAsync(int id)
        {
            return await _context.LLMChannels
                .Include(c => c.Models)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<LLMChannel>> GetByNameAsync(string name)
        {
            return await _context.LLMChannels
                .Include(c => c.Models)
                .Where(c => c.Name.Contains(name))
                .OrderBy(c => c.Priority)
                .ToListAsync();
        }

        public async Task<List<LLMChannel>> GetByProviderAsync(LLMProvider provider)
        {
            return await _context.LLMChannels
                .Include(c => c.Models)
                .Where(c => c.Provider == provider)
                .OrderBy(c => c.Priority)
                .ToListAsync();
        }

        public async Task<List<LLMChannel>> GetAllAsync(int skip = 0, int take = 50)
        {
            return await _context.LLMChannels
                .Include(c => c.Models)
                .OrderBy(c => c.Priority)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<LLMChannel>> GetAvailableChannelsAsync(int maxCount = 10)
        {
            return await _context.LLMChannels
                .Include(c => c.Models)
                .Where(c => !string.IsNullOrEmpty(c.ApiKey) && !string.IsNullOrEmpty(c.Gateway))
                .OrderByDescending(c => c.Priority)
                .Take(maxCount)
                .ToListAsync();
        }

        public async Task<LLMChannel> AddAsync(LLMChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            await _context.LLMChannels.AddAsync(channel);
            await _context.SaveChangesAsync();
            return channel;
        }

        public async Task<LLMChannel> UpdateAsync(LLMChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            _context.LLMChannels.Update(channel);
            await _context.SaveChangesAsync();
            return channel;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var channel = await _context.LLMChannels.FindAsync(id);
            if (channel == null)
                return false;

            _context.LLMChannels.Remove(channel);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdatePriorityAsync(int id, int priority)
        {
            var channel = await _context.LLMChannels.FindAsync(id);
            if (channel == null)
                return false;

            channel.Priority = priority;
            await _context.SaveChangesAsync();
            return true;
        }
    }

    /// <summary>
    /// 搜索页面缓存查询服务实现
    /// </summary>
    public class SearchPageCacheQueryService : ISearchPageCacheQueryService
    {
        private readonly DataDbContext _context;

        public SearchPageCacheQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<SearchPageCache?> GetByUUIDAsync(string uuid)
        {
            return await _context.SearchPageCaches
                .FirstOrDefaultAsync(c => c.UUID == uuid);
        }

        public async Task<SearchPageCache> AddAsync(SearchPageCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            await _context.SearchPageCaches.AddAsync(cache);
            await _context.SaveChangesAsync();
            return cache;
        }

        public async Task<SearchPageCache> UpdateAsync(SearchPageCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            _context.SearchPageCaches.Update(cache);
            await _context.SaveChangesAsync();
            return cache;
        }

        public async Task<bool> DeleteAsync(string uuid)
        {
            var cache = await _context.SearchPageCaches
                .FirstOrDefaultAsync(c => c.UUID == uuid);
            if (cache == null)
                return false;

            _context.SearchPageCaches.Remove(cache);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> CleanExpiredCacheAsync(TimeSpan expiration)
        {
            var expiredCaches = await _context.SearchPageCaches
                .Where(c => DateTime.UtcNow - c.CreatedTime > expiration)
                .ToListAsync();

            if (expiredCaches.Count == 0)
                return 0;

            _context.SearchPageCaches.RemoveRange(expiredCaches);
            await _context.SaveChangesAsync();
            return expiredCaches.Count;
        }

        public async Task<int> GetCacheCountAsync()
        {
            return await _context.SearchPageCaches.CountAsync();
        }
    }

    /// <summary>
    /// 对话段查询服务实现
    /// </summary>
    public class ConversationSegmentQueryService : IConversationSegmentQueryService
    {
        private readonly DataDbContext _context;

        public ConversationSegmentQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<ConversationSegment?> GetByIdAsync(long id)
        {
            return await _context.ConversationSegments
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<List<ConversationSegment>> GetByGroupIdAsync(long groupId, int skip = 0, int take = 50)
        {
            return await _context.ConversationSegments
                .Include(s => s.Messages)
                .Where(s => s.GroupId == groupId)
                .OrderByDescending(s => s.StartTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<ConversationSegment>> GetByTimeRangeAsync(long groupId, DateTime startTime, DateTime endTime, int skip = 0, int take = 50)
        {
            return await _context.ConversationSegments
                .Include(s => s.Messages)
                .Where(s => s.GroupId == groupId && s.StartTime >= startTime && s.EndTime <= endTime)
                .OrderBy(s => s.StartTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<ConversationSegment>> GetByVectorIdAsync(string vectorId, int skip = 0, int take = 50)
        {
            return await _context.ConversationSegments
                .Include(s => s.Messages)
                .Where(s => s.VectorId == vectorId)
                .OrderByDescending(s => s.StartTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<ConversationSegment>> SearchAsync(long groupId, string searchTerm, int skip = 0, int take = 50)
        {
            return await _context.ConversationSegments
                .Include(s => s.Messages)
                .Where(s => s.GroupId == groupId && 
                           (s.ContentSummary.Contains(searchTerm) || 
                            s.TopicKeywords.Contains(searchTerm) || 
                            s.FullContent.Contains(searchTerm)))
                .OrderByDescending(s => s.StartTime)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<ConversationSegment> AddAsync(ConversationSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            await _context.ConversationSegments.AddAsync(segment);
            await _context.SaveChangesAsync();
            return segment;
        }

        public async Task<List<ConversationSegment>> AddRangeAsync(List<ConversationSegment> segments)
        {
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));

            await _context.ConversationSegments.AddRangeAsync(segments);
            await _context.SaveChangesAsync();
            return segments;
        }

        public async Task<ConversationSegment> UpdateAsync(ConversationSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));

            _context.ConversationSegments.Update(segment);
            await _context.SaveChangesAsync();
            return segment;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var segment = await _context.ConversationSegments.FindAsync(id);
            if (segment == null)
                return false;

            _context.ConversationSegments.Remove(segment);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetCountAsync(long groupId)
        {
            return await _context.ConversationSegments
                .CountAsync(s => s.GroupId == groupId);
        }
    }

    /// <summary>
    /// 向量索引查询服务实现
    /// </summary>
    public class VectorIndexQueryService : IVectorIndexQueryService
    {
        private readonly DataDbContext _context;

        public VectorIndexQueryService(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<VectorIndex?> GetByIdAsync(long id)
        {
            return await _context.VectorIndexes
                .FirstOrDefaultAsync(v => v.Id == id);
        }

        public async Task<List<VectorIndex>> GetByGroupIdAndTypeAsync(long groupId, string vectorType, int skip = 0, int take = 50)
        {
            return await _context.VectorIndexes
                .Where(v => v.GroupId == groupId && v.VectorType == vectorType)
                .OrderBy(v => v.EntityId)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<VectorIndex>> GetByEntityIdAsync(long groupId, string vectorType, long entityId, int skip = 0, int take = 50)
        {
            return await _context.VectorIndexes
                .Where(v => v.GroupId == groupId && v.VectorType == vectorType && v.EntityId == entityId)
                .OrderByDescending(v => v.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<VectorIndex>> GetByFaissIndexAsync(long groupId, long faissIndex, int skip = 0, int take = 50)
        {
            return await _context.VectorIndexes
                .Where(v => v.GroupId == groupId && v.FaissIndex == faissIndex)
                .OrderBy(v => v.EntityId)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<VectorIndex>> GetAllAsync(int skip = 0, int take = 50)
        {
            return await _context.VectorIndexes
                .OrderBy(v => v.GroupId)
                .ThenBy(v => v.VectorType)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<VectorIndex> AddAsync(VectorIndex vectorIndex)
        {
            if (vectorIndex == null)
                throw new ArgumentNullException(nameof(vectorIndex));

            await _context.VectorIndexes.AddAsync(vectorIndex);
            await _context.SaveChangesAsync();
            return vectorIndex;
        }

        public async Task<List<VectorIndex>> AddRangeAsync(List<VectorIndex> vectorIndices)
        {
            if (vectorIndices == null)
                throw new ArgumentNullException(nameof(vectorIndices));

            await _context.VectorIndexes.AddRangeAsync(vectorIndices);
            await _context.SaveChangesAsync();
            return vectorIndices;
        }

        public async Task<VectorIndex> UpdateAsync(VectorIndex vectorIndex)
        {
            if (vectorIndex == null)
                throw new ArgumentNullException(nameof(vectorIndex));

            _context.VectorIndexes.Update(vectorIndex);
            await _context.SaveChangesAsync();
            return vectorIndex;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var vectorIndex = await _context.VectorIndexes.FindAsync(id);
            if (vectorIndex == null)
                return false;

            _context.VectorIndexes.Remove(vectorIndex);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> DeleteByGroupIdAsync(long groupId)
        {
            var vectorIndices = await _context.VectorIndexes
                .Where(v => v.GroupId == groupId)
                .ToListAsync();

            if (vectorIndices.Count == 0)
                return 0;

            _context.VectorIndexes.RemoveRange(vectorIndices);
            await _context.SaveChangesAsync();
            return vectorIndices.Count;
        }

        public async Task<int> GetCountAsync(long groupId)
        {
            return await _context.VectorIndexes
                .CountAsync(v => v.GroupId == groupId);
        }
    }
}