using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface
{
    public interface IShortUrlMappingService : IService
    {
        Task<int> SaveUrlMappingsAsync(IEnumerable<ShortUrlMapping> mappings, CancellationToken token);
        Task<Dictionary<string, string>> GetUrlMappingsAsync(IEnumerable<string> originalUrls, CancellationToken token);
    }
}
