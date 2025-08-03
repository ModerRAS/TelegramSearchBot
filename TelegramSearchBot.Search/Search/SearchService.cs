using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service.Search
{
    /// <summary>
    /// 搜索服务 - 简化实现版本
    /// 专注于Lucene搜索功能，其他依赖暂时注释
    /// </summary>
    public class SearchService 
    {
        private readonly LuceneManager lucene;
        private readonly DataDbContext dbContext;
        
        public SearchService(DataDbContext dbContext)
        {
            this.lucene = new LuceneManager();
            this.dbContext = dbContext;
        }

        public async Task<TelegramSearchBot.Model.SearchOption> Search(TelegramSearchBot.Model.SearchOption searchOption)
        {
            return searchOption.SearchType switch
            {
                SearchType.InvertedIndex => await LuceneSearch(searchOption), // 默认使用简单搜索
                SearchType.SyntaxSearch => await LuceneSyntaxSearch(searchOption), // 语法搜索
                _ => await LuceneSearch(searchOption) // 默认使用简单搜索
            };
        }

        private async Task<TelegramSearchBot.Model.SearchOption> LuceneSearch(TelegramSearchBot.Model.SearchOption searchOption)
        {
            if (searchOption.IsGroup)
            {
                var result = await lucene.Search(searchOption.Search, searchOption.ChatId, searchOption.Skip, searchOption.Take);
                searchOption.Count = result.Item1;
                searchOption.Messages = result.Item2;
            }
            else
            {
                var result = await lucene.SearchAll(searchOption.Search, searchOption.Skip, searchOption.Take);
                searchOption.Count = result.Item1;
                searchOption.Messages = result.Item2;
            }
            return searchOption;
        }
        
        // 语法搜索方法 - 使用支持语法的新搜索实现
        private async Task<TelegramSearchBot.Model.SearchOption> LuceneSyntaxSearch(TelegramSearchBot.Model.SearchOption searchOption)
        {
            if (searchOption.IsGroup)
            {
                var result = await lucene.SyntaxSearch(searchOption.Search, searchOption.ChatId, searchOption.Skip, searchOption.Take);
                searchOption.Count = result.Item1;
                searchOption.Messages = result.Item2;
            }
            else
            {
                var result = await lucene.SyntaxSearchAll(searchOption.Search, searchOption.Skip, searchOption.Take);
                searchOption.Count = result.Item1;
                searchOption.Messages = result.Item2;
            }
            return searchOption;
        }

        // 向量搜索方法 - 暂时注释，待Vector模块重构时实现
        /*
        private async Task<TelegramSearchBot.Model.SearchOption> VectorSearch(TelegramSearchBot.Model.SearchOption searchOption)
        {
            if (searchOption.IsGroup)
            {
                // 使用FAISS对话段向量搜索当前群组
                // 待Vector模块重构完成后实现
            }
            return searchOption;
        }
        */

        // 简单搜索方法 - 提供向后兼容性
        public async Task<TelegramSearchBot.Model.SearchOption> SimpleSearch(TelegramSearchBot.Model.SearchOption searchOption)
        {
            return await LuceneSearch(searchOption);
        }
    }
}