using System.Threading.Tasks;
using TelegramSearchBot.Model.Bilibili;

namespace TelegramSearchBot.Service.Bilibili;

public interface IBiliApiService
{
    /// <summary>
    /// Gets detailed information for a Bilibili video from its URL.
    /// </summary>
    /// <param name="videoUrl">The URL of the Bilibili video.</param>
    /// <returns>A BiliVideoInfo object containing video details, or null if an error occurs.</returns>
    Task<BiliVideoInfo> GetVideoInfoAsync(string videoUrl);

    /// <summary>
    /// Gets detailed information for a Bilibili opus (dynamic/feed item) from its URL.
    /// </summary>
    /// <param name="opusUrl">The URL of the Bilibili opus.</param>
    /// <returns>A BiliOpusInfo object containing opus details, or null if an error occurs.</returns>
    Task<BiliOpusInfo> GetOpusInfoAsync(string opusUrl);

    /// <summary>
    /// 获取B站专栏（文章）信息。
    /// </summary>
    /// <param name="articleUrl">专栏URL</param>
    /// <returns>BiliArticleInfo对象，获取失败返回null</returns>
    Task<BiliArticleInfo> GetArticleInfoAsync(string articleUrl);
}
