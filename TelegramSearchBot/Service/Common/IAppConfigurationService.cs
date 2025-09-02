using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Common;

public interface IAppConfigurationService {
    Task<string> GetConfigurationValueAsync(string key);
    Task SetConfigurationValueAsync(string key, string value);
}
