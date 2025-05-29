using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools
{
    public interface IShortUrlToolService
    {
        Task<ShortUrlMappingResult> GetShortUrlMapping(string shortUrl);
        Task<List<ShortUrlMappingResult>> GetAllShortUrlMappings(
            string originalUrlQuery = null,
            string expandedUrlQuery = null,
            int page = 1,
            int pageSize = 10);
        Task<List<ShortUrlMappingResult>> GetShortUrlMappingsBatch(List<string> shortUrls);
    }
} 