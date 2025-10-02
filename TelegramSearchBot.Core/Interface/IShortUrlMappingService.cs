using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Interface {
    public interface IShortUrlMappingService : IService {
        Task<int> SaveUrlMappingsAsync(IEnumerable<ShortUrlMapping> mappings, CancellationToken token);
        Task<Dictionary<string, string>> GetUrlMappingsAsync(IEnumerable<string> originalUrls, CancellationToken token);
    }
}
