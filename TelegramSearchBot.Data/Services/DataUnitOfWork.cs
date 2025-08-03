using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Data.Interfaces;
using TelegramSearchBot.Data.Services;

namespace TelegramSearchBot.Data.Services
{
    /// <summary>
    /// 数据库Unit of Work实现
    /// </summary>
    public class DataUnitOfWork : IDataUnitOfWork
    {
        private readonly DataDbContext _context;
        private IDbContextTransaction _transaction;
        private bool _disposed;

        // 查询服务实例
        private IMessageQueryService _messages;
        private IUserQueryService _users;
        private IGroupQueryService _groups;
        private ILLMChannelQueryService _llmChannels;
        private ISearchPageCacheQueryService _searchPageCaches;
        private IConversationSegmentQueryService _conversationSegments;
        private IVectorIndexQueryService _vectorIndices;

        public DataUnitOfWork(DataDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public IMessageQueryService Messages
        {
            get { return _messages ??= new MessageQueryService(_context); }
        }

        public IUserQueryService Users
        {
            get { return _users ??= new UserQueryService(_context); }
        }

        public IGroupQueryService Groups
        {
            get { return _groups ??= new GroupQueryService(_context); }
        }

        public ILLMChannelQueryService LLMChannels
        {
            get { return _llmChannels ??= new LLMChannelQueryService(_context); }
        }

        public ISearchPageCacheQueryService SearchPageCaches
        {
            get { return _searchPageCaches ??= new SearchPageCacheQueryService(_context); }
        }

        public IConversationSegmentQueryService ConversationSegments
        {
            get { return _conversationSegments ??= new ConversationSegmentQueryService(_context); }
        }

        public IVectorIndexQueryService VectorIndices
        {
            get { return _vectorIndices ??= new VectorIndexQueryService(_context); }
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            if (_transaction != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress");
            }

            try
            {
                await _context.SaveChangesAsync();
                await _transaction.CommitAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress");
            }

            try
            {
                await _transaction.RollbackAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放事务
                    if (_transaction != null)
                    {
                        _transaction.Dispose();
                        _transaction = null;
                    }

                    // 释放数据库上下文
                    _context.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}