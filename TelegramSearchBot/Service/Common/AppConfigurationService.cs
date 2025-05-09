using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // For IServiceScopeFactory
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model; // For DataDbContext
using TelegramSearchBot.Model.Data; // For AppConfigurationItem

namespace TelegramSearchBot.Service.Common;

public class AppConfigurationService : IAppConfigurationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppConfigurationService> _logger;
    public const string BiliCookieKey = "BiliCookie"; // Define a constant for the key

    public AppConfigurationService(IServiceScopeFactory scopeFactory, ILogger<AppConfigurationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> GetConfigurationValueAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
        
        var configItem = await dbContext.AppConfigurationItems.FindAsync(key);
        return configItem?.Value;
    }

    public async Task SetConfigurationValueAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Configuration key cannot be null or empty.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

        var configItem = await dbContext.AppConfigurationItems.FindAsync(key);
        if (configItem != null)
        {
            configItem.Value = value;
            _logger.LogInformation("Updating configuration item with key: {Key}", key);
        }
        else
        {
            dbContext.AppConfigurationItems.Add(new AppConfigurationItem { Key = key, Value = value });
            _logger.LogInformation("Adding new configuration item with key: {Key}", key);
        }
        await dbContext.SaveChangesAsync();
    }
}
