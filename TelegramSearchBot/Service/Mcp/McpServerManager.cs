using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Service.Mcp {
    /// <summary>
    /// Manages MCP server configurations and lifecycle.
    /// Stores server configs in a JSON file and manages client connections.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class McpServerManager : IMcpServerManager, IService, IDisposable {
        private readonly ILogger<McpServerManager> _logger;
        private readonly ConcurrentDictionary<string, IMcpClient> _clients = new();
        private readonly ConcurrentDictionary<string, List<McpToolDescription>> _serverTools = new();
        private readonly ConcurrentDictionary<string, string> _toolToServer = new();
        private readonly string _configPath;
        private McpServersConfig _config;

        public string ServiceName => "McpServerManager";

        public McpServerManager(ILogger<McpServerManager> logger) {
            _logger = logger;
            _configPath = Path.Combine(Env.WorkDir, "mcp_servers.json");
            LoadConfig();
        }

        private void LoadConfig() {
            try {
                if (File.Exists(_configPath)) {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<McpServersConfig>(json) ?? new McpServersConfig();
                } else {
                    _config = new McpServersConfig();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to load MCP server config from {Path}", _configPath);
                _config = new McpServersConfig();
            }
        }

        private async Task SaveConfigAsync() {
            try {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                await File.WriteAllTextAsync(_configPath, json);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to save MCP server config to {Path}", _configPath);
            }
        }

        public List<McpServerConfig> GetServerConfigs() {
            return _config.Servers.ToList();
        }

        public async Task AddServerAsync(McpServerConfig config) {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.Name)) throw new ArgumentException("Server name is required.");

            // Remove existing server with same name
            _config.Servers.RemoveAll(s => s.Name.Equals(config.Name, StringComparison.OrdinalIgnoreCase));
            _config.Servers.Add(config);
            await SaveConfigAsync();

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

            _config.Servers.RemoveAll(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            await SaveConfigAsync();

            _logger.LogInformation("Removed MCP server: {Name}", serverName);
        }

        public async Task InitializeAllServersAsync(CancellationToken cancellationToken = default) {
            foreach (var serverConfig in _config.Servers.Where(s => s.Enabled)) {
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
            // Best-effort synchronous cleanup to avoid deadlocks
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
