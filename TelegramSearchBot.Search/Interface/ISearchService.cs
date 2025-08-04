using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 搜索服务接口
    /// 定义统一的消息搜索功能
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// 执行搜索操作
        /// 根据搜索类型自动选择对应的搜索实现
        /// </summary>
        /// <param name="searchOption">搜索选项</param>
        /// <returns>搜索结果</returns>
        Task<TelegramSearchBot.Model.SearchOption> Search(TelegramSearchBot.Model.SearchOption searchOption);

        /// <summary>
        /// 执行简单搜索（向后兼容性）
        /// </summary>
        /// <param name="searchOption">搜索选项</param>
        /// <returns>搜索结果</returns>
        Task<TelegramSearchBot.Model.SearchOption> SimpleSearch(TelegramSearchBot.Model.SearchOption searchOption);
    }
}