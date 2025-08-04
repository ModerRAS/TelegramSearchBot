using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Data.Interfaces
{
    /// <summary>
    /// 消息数据查询服务接口
    /// </summary>
    public interface IMessageQueryService
    {
        /// <summary>
        /// 根据ID获取消息
        /// </summary>
        Task<Message?> GetByIdAsync(long id);
        
        /// <summary>
        /// 根据群组ID和消息ID获取消息
        /// </summary>
        Task<Message?> GetByGroupAndMessageIdAsync(long groupId, long messageId);
        
        /// <summary>
        /// 根据群组ID获取消息列表
        /// </summary>
        Task<List<Message>> GetByGroupIdAsync(long groupId, int skip = 0, int take = 50);
        
        /// <summary>
        /// 根据用户ID获取消息列表
        /// </summary>
        Task<List<Message>> GetByUserIdAsync(long userId, int skip = 0, int take = 50);
        
        /// <summary>
        /// 搜索消息内容
        /// </summary>
        Task<List<Message>> SearchContentAsync(long groupId, string searchTerm, int skip = 0, int take = 50);
        
        /// <summary>
        /// 获取时间范围内的消息
        /// </summary>
        Task<List<Message>> GetByTimeRangeAsync(long groupId, DateTime startTime, DateTime endTime, int skip = 0, int take = 50);
        
        /// <summary>
        /// 添加新消息
        /// </summary>
        Task<Message> AddAsync(Message message);
        
        /// <summary>
        /// 批量添加消息
        /// </summary>
        Task<List<Message>> AddRangeAsync(List<Message> messages);
        
        /// <summary>
        /// 更新消息
        /// </summary>
        Task<Message> UpdateAsync(Message message);
        
        /// <summary>
        /// 删除消息
        /// </summary>
        Task<bool> DeleteAsync(long id);
        
        /// <summary>
        /// 获取消息总数
        /// </summary>
        Task<int> GetCountAsync(long groupId);
    }

    /// <summary>
    /// 用户数据查询服务接口
    /// </summary>
    public interface IUserQueryService
    {
        /// <summary>
        /// 根据ID获取用户
        /// </summary>
        Task<UserData?> GetByIdAsync(long id);
        
        /// <summary>
        /// 根据用户名获取用户
        /// </summary>
        Task<UserData?> GetByUserNameAsync(string userName);
        
        /// <summary>
        /// 获取所有用户
        /// </summary>
        Task<List<UserData>> GetAllAsync(int skip = 0, int take = 50);
        
        /// <summary>
        /// 搜索用户
        /// </summary>
        Task<List<UserData>> SearchAsync(string searchTerm, int skip = 0, int take = 50);
        
        /// <summary>
        /// 添加用户
        /// </summary>
        Task<UserData> AddAsync(UserData user);
        
        /// <summary>
        /// 更新用户
        /// </summary>
        Task<UserData> UpdateAsync(UserData user);
        
        /// <summary>
        /// 删除用户
        /// </summary>
        Task<bool> DeleteAsync(long id);
    }

    /// <summary>
    /// 群组数据查询服务接口
    /// </summary>
    public interface IGroupQueryService
    {
        /// <summary>
        /// 根据ID获取群组
        /// </summary>
        Task<GroupData?> GetByIdAsync(long id);
        
        /// <summary>
        /// 获取所有群组
        /// </summary>
        Task<List<GroupData>> GetAllAsync(int skip = 0, int take = 50);
        
        /// <summary>
        /// 搜索群组
        /// </summary>
        Task<List<GroupData>> SearchAsync(string searchTerm, int skip = 0, int take = 50);
        
        /// <summary>
        /// 获取非黑名单群组
        /// </summary>
        Task<List<GroupData>> GetNonBlacklistGroupsAsync(int skip = 0, int take = 50);
        
        /// <summary>
        /// 添加群组
        /// </summary>
        Task<GroupData> AddAsync(GroupData group);
        
        /// <summary>
        /// 更新群组
        /// </summary>
        Task<GroupData> UpdateAsync(GroupData group);
        
        /// <summary>
        /// 设置群组黑名单状态
        /// </summary>
        Task<bool> SetBlacklistStatusAsync(long groupId, bool isBlacklist);
        
        /// <summary>
        /// 删除群组
        /// </summary>
        Task<bool> DeleteAsync(long id);
    }

    /// <summary>
    /// LLM通道查询服务接口
    /// </summary>
    public interface ILLMChannelQueryService
    {
        /// <summary>
        /// 根据ID获取LLM通道
        /// </summary>
        Task<LLMChannel?> GetByIdAsync(int id);
        
        /// <summary>
        /// 根据名称获取LLM通道
        /// </summary>
        Task<List<LLMChannel>> GetByNameAsync(string name);
        
        /// <summary>
        /// 根据提供商获取LLM通道
        /// </summary>
        Task<List<LLMChannel>> GetByProviderAsync(LLMProvider provider);
        
        /// <summary>
        /// 获取所有LLM通道
        /// </summary>
        Task<List<LLMChannel>> GetAllAsync(int skip = 0, int take = 50);
        
        /// <summary>
        /// 获取可用的LLM通道（按优先级排序）
        /// </summary>
        Task<List<LLMChannel>> GetAvailableChannelsAsync(int maxCount = 10);
        
        /// <summary>
        /// 添加LLM通道
        /// </summary>
        Task<LLMChannel> AddAsync(LLMChannel channel);
        
        /// <summary>
        /// 更新LLM通道
        /// </summary>
        Task<LLMChannel> UpdateAsync(LLMChannel channel);
        
        /// <summary>
        /// 删除LLM通道
        /// </summary>
        Task<bool> DeleteAsync(int id);
        
        /// <summary>
        /// 更新通道优先级
        /// </summary>
        Task<bool> UpdatePriorityAsync(int id, int priority);
    }

    /// <summary>
    /// 搜索页面缓存查询服务接口
    /// </summary>
    public interface ISearchPageCacheQueryService
    {
        /// <summary>
        /// 根据UUID获取搜索选项缓存
        /// </summary>
        Task<SearchPageCache?> GetByUUIDAsync(string uuid);
        
        /// <summary>
        /// 添加搜索选项缓存
        /// </summary>
        Task<SearchPageCache> AddAsync(SearchPageCache cache);
        
        /// <summary>
        /// 更新搜索选项缓存
        /// </summary>
        Task<SearchPageCache> UpdateAsync(SearchPageCache cache);
        
        /// <summary>
        /// 删除搜索选项缓存
        /// </summary>
        Task<bool> DeleteAsync(string uuid);
        
        /// <summary>
        /// 清理过期缓存
        /// </summary>
        Task<int> CleanExpiredCacheAsync(TimeSpan expiration);
        
        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        Task<int> GetCacheCountAsync();
    }

    /// <summary>
    /// 对话段查询服务接口
    /// </summary>
    public interface IConversationSegmentQueryService
    {
        /// <summary>
        /// 根据ID获取对话段
        /// </summary>
        Task<ConversationSegment?> GetByIdAsync(long id);
        
        /// <summary>
        /// 根据群组ID获取对话段
        /// </summary>
        Task<List<ConversationSegment>> GetByGroupIdAsync(long groupId, int skip = 0, int take = 50);
        
        /// <summary>
        /// 根据时间范围获取对话段
        /// </summary>
        Task<List<ConversationSegment>> GetByTimeRangeAsync(long groupId, DateTime startTime, DateTime endTime, int skip = 0, int take = 50);
        
        /// <summary>
        /// 根据向量ID获取对话段
        /// </summary>
        Task<List<ConversationSegment>> GetByVectorIdAsync(string vectorId, int skip = 0, int take = 50);
        
        /// <summary>
        /// 搜索对话段内容
        /// </summary>
        Task<List<ConversationSegment>> SearchAsync(long groupId, string searchTerm, int skip = 0, int take = 50);
        
        /// <summary>
        /// 添加对话段
        /// </summary>
        Task<ConversationSegment> AddAsync(ConversationSegment segment);
        
        /// <summary>
        /// 批量添加对话段
        /// </summary>
        Task<List<ConversationSegment>> AddRangeAsync(List<ConversationSegment> segments);
        
        /// <summary>
        /// 更新对话段
        /// </summary>
        Task<ConversationSegment> UpdateAsync(ConversationSegment segment);
        
        /// <summary>
        /// 删除对话段
        /// </summary>
        Task<bool> DeleteAsync(long id);
        
        /// <summary>
        /// 获取对话段总数
        /// </summary>
        Task<int> GetCountAsync(long groupId);
    }

    /// <summary>
    /// 向量索引查询服务接口
    /// </summary>
    public interface IVectorIndexQueryService
    {
        /// <summary>
        /// 根据ID获取向量索引
        /// </summary>
        Task<VectorIndex?> GetByIdAsync(long id);
        
        /// <summary>
        /// 根据群组ID和向量类型获取向量索引
        /// </summary>
        Task<List<VectorIndex>> GetByGroupIdAndTypeAsync(long groupId, string vectorType, int skip = 0, int take = 50);
        
        /// <summary>
        /// 根据实体ID获取向量索引
        /// </summary>
        Task<List<VectorIndex>> GetByEntityIdAsync(long groupId, string vectorType, long entityId, int skip = 0, int take = 50);
        
        /// <summary>
        /// 根据Faiss索引获取向量索引
        /// </summary>
        Task<List<VectorIndex>> GetByFaissIndexAsync(long groupId, long faissIndex, int skip = 0, int take = 50);
        
        /// <summary>
        /// 获取所有向量索引
        /// </summary>
        Task<List<VectorIndex>> GetAllAsync(int skip = 0, int take = 50);
        
        /// <summary>
        /// 添加向量索引
        /// </summary>
        Task<VectorIndex> AddAsync(VectorIndex vectorIndex);
        
        /// <summary>
        /// 批量添加向量索引
        /// </summary>
        Task<List<VectorIndex>> AddRangeAsync(List<VectorIndex> vectorIndices);
        
        /// <summary>
        /// 更新向量索引
        /// </summary>
        Task<VectorIndex> UpdateAsync(VectorIndex vectorIndex);
        
        /// <summary>
        /// 删除向量索引
        /// </summary>
        Task<bool> DeleteAsync(long id);
        
        /// <summary>
        /// 根据群组ID删除向量索引
        /// </summary>
        Task<int> DeleteByGroupIdAsync(long groupId);
        
        /// <summary>
        /// 获取向量索引总数
        /// </summary>
        Task<int> GetCountAsync(long groupId);
    }

    /// <summary>
    /// 数据库单元OfWork接口
    /// </summary>
    public interface IDataUnitOfWork : IDisposable
    {
        /// <summary>
        /// 消息查询服务
        /// </summary>
        IMessageQueryService Messages { get; }
        
        /// <summary>
        /// 用户查询服务
        /// </summary>
        IUserQueryService Users { get; }
        
        /// <summary>
        /// 群组查询服务
        /// </summary>
        IGroupQueryService Groups { get; }
        
        /// <summary>
        /// LLM通道查询服务
        /// </summary>
        ILLMChannelQueryService LLMChannels { get; }
        
        /// <summary>
        /// 搜索页面缓存查询服务
        /// </summary>
        ISearchPageCacheQueryService SearchPageCaches { get; }
        
        /// <summary>
        /// 对话段查询服务
        /// </summary>
        IConversationSegmentQueryService ConversationSegments { get; }
        
        /// <summary>
        /// 向量索引查询服务
        /// </summary>
        IVectorIndexQueryService VectorIndices { get; }
        
        /// <summary>
        /// 保存所有更改
        /// </summary>
        Task<int> SaveChangesAsync();
        
        /// <summary>
        /// 开始事务
        /// </summary>
        Task BeginTransactionAsync();
        
        /// <summary>
        /// 提交事务
        /// </summary>
        Task CommitTransactionAsync();
        
        /// <summary>
        /// 回滚事务
        /// </summary>
        Task RollbackTransactionAsync();
    }
}