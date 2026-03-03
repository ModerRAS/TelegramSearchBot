using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Service.Mcp {
    /// <summary>
    /// Manages MCP server configurations and lifecycle.
    /// Stores server configs in the SQLite database via AppConfigurationItems table.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class McpServerManager : IMcpServerManager, IService, IDisposable {
        private readonly ILogger<McpServerManager> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, IMcpClient> _clients = new();
        private readonly ConcurrentDictionary<string, List<McpToolDescription>> _serverTools = new();
        private readonly ConcurrentDictionary<string, string> _toolToServer = new();

        internal const string McpConfigKeyPrefix = "MCP:ServerConfig:";

        public string ServiceName => "McpServerManager";

        public McpServerManager(ILogger<McpServerManager> logger, IServiceScopeFactory scopeFactory) {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private async Task<List<McpServerConfig>> LoadConfigsFromDbAsync() {
            try {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

                var items = await dbContext.AppConfigurationItems
                    .Where(x => x.Key.StartsWith(McpConfigKeyPrefix))
                    .ToListAsync();

                var configs = new List<McpServerConfig>();
                foreach (var item in items) {
                    try {
                        var config = JsonConvert.DeserializeObject<McpServerConfig>(item.Value);
                        if (config != null) {
                            configs.Add(config);
                        }
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to deserialize MCP server config for key {Key}", item.Key);
                    }
                }
                return configs;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to load MCP server configs from database");
                return new List<McpServerConfig>();
            }
        }

        private async Task SaveConfigToDbAsync(McpServerConfig config) {
            try {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

                var key = McpConfigKeyPrefix + config.Name;
                var json = JsonConvert.SerializeObject(config);

                var existing = await dbContext.AppConfigurationItems.FindAsync(key);
                if (existing != null) {
                    existing.Value = json;
                } else {
                    dbContext.AppConfigurationItems.Add(new TelegramSearchBot.Model.Data.AppConfigurationItem {
                        Key = key,
                        Value = json
                    });
                }
                await dbContext.SaveChangesAsync();
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to save MCP server config for {Name}", config.Name);
            }
        }

        private async Task RemoveConfigFromDbAsync(string serverName) {
            try {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();

                var key = McpConfigKeyPrefix + serverName;
                var existing = await dbContext.AppConfigurationItems.FindAsync(key);
                if (existing != null) {
                    dbContext.AppConfigurationItems.Remove(existing);
                    await dbContext.SaveChangesAsync();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to remove MCP server config for {Name}", serverName);
            }
        }

        public List<McpServerConfig> GetServerConfigs() {
            return LoadConfigsFromDbAsync().GetAwaiter().GetResult();
        }

        public async Task<List<McpServerConfig>> GetServerConfigsAsync() {
            return await LoadConfigsFromDbAsync();
        }

        public async Task AddServerAsync(McpServerConfig config) {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.Name)) throw new ArgumentException("Server name is required.");

            await SaveConfigToDbAsync(config);

            _logger.LogInformation("Added MCP server config: {Name} ({Command})", config.Name, config.Command);

            // Try to connect to the new server
            if (config.Enabled) {
                try {
                    await ConnectToServerAsync(config);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to connect to newly added MCP server '{Name}'", config.Name);
                }
            }
        }

        public async Task RemoveServerAsync(string serverName) {
            // Disconnect if connected
            if (_clients.TryRemove(serverName, out var client)) {
                try {
                    await client.DisconnectAsync();
                    (client as IDisposable)?.Dispose();
                } catch { }
            }

            // Remove tool mappings
            if (_serverTools.TryRemove(serverName, out var tools)) {
                foreach (var tool in tools) {
                    _toolToServer.TryRemove(tool.Name, out _);
                }
            }

            await RemoveConfigFromDbAsync(serverName);

            _logger.LogInformation("Removed MCP server: {Name}", serverName);
        }

        public async Task InitializeAllServersAsync(CancellationToken cancellationToken = default) {
            var configs = await LoadConfigsFromDbAsync();
            foreach (var serverConfig in configs.Where(s => s.Enabled)) {
                try {
                    await ConnectToServerAsync(serverConfig, cancellationToken);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to initialize MCP server '{Name}'. It will be skipped.", serverConfig.Name);
                }
            }

            _logger.LogInformation("MCP server initialization complete. {Count} servers connected, {ToolCount} tools available.",
                _clients.Count, _toolToServer.Count);
        }

        private async Task ConnectToServerAsync(McpServerConfig config, CancellationToken cancellationToken = default) {
            var client = new McpClient(config, _logger);
            await client.ConnectAsync(cancellationToken);

            var tools = await client.ListToolsAsync(cancellationToken);
            _clients[config.Name] = client;
            _serverTools[config.Name] = tools;

            foreach (var tool in tools) {
                var qualifiedName = $"mcp_{config.Name}_{tool.Name}";
                _toolToServer[qualifiedName] = config.Name;
                _logger.LogInformation("Registered external MCP tool: {ToolName} from server '{ServerName}'",
                    qualifiedName, config.Name);
            }
        }

        public List<(string serverName, McpToolDescription tool)> GetAllExternalTools() {
            var result = new List<(string, McpToolDescription)>();
            foreach (var kvp in _serverTools) {
                foreach (var tool in kvp.Value) {
                    result.Add((kvp.Key, tool));
                }
            }
            return result;
        }

        public async Task<McpToolCallResult> CallToolAsync(string serverName, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default) {
            if (!_clients.TryGetValue(serverName, out var client)) {
                throw new InvalidOperationException($"MCP server '{serverName}' is not connected.");
            }

            return await client.CallToolAsync(toolName, arguments, cancellationToken);
        }

        public string FindServerForTool(string qualifiedToolName) {
            _toolToServer.TryGetValue(qualifiedToolName, out var serverName);
            return serverName;
        }

        public async Task ShutdownAllAsync() {
            foreach (var kvp in _clients) {
                try {
                    await kvp.Value.DisconnectAsync();
                    (kvp.Value as IDisposable)?.Dispose();
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Error disconnecting MCP server '{Name}'", kvp.Key);
                }
            }

            _clients.Clear();
            _serverTools.Clear();
            _toolToServer.Clear();
        }

        public void Dispose() {
            foreach (var kvp in _clients) {
                try {
                    (kvp.Value as IDisposable)?.Dispose();
                } catch { }
            }
            _clients.Clear();
            _serverTools.Clear();
            _toolToServer.Clear();
        }
    }
}
