using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Interface.Mcp {
    /// <summary>
    /// Interface for managing MCP server configurations and lifecycle.
    /// </summary>
    public interface IMcpServerManager {
        /// <summary>
        /// Get all configured MCP servers asynchronously.
        /// </summary>
        Task<List<McpServerConfig>> GetServerConfigsAsync();

        /// <summary>
        /// Add a new MCP server configuration.
        /// </summary>
        Task AddServerAsync(McpServerConfig config);

        /// <summary>
        /// Remove an MCP server configuration by name.
        /// </summary>
        Task RemoveServerAsync(string serverName);

        /// <summary>
        /// Start all enabled MCP servers and discover their tools.
        /// </summary>
        Task InitializeAllServersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all tools from all connected MCP servers.
        /// </summary>
        List<(string serverName, McpToolDescription tool)> GetAllExternalTools();

        /// <summary>
        /// Call a tool on a specific MCP server.
        /// </summary>
        Task<McpToolCallResult> CallToolAsync(string serverName, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Find which server hosts a given tool name.
        /// Returns the server name, or null if not found.
        /// </summary>
        string FindServerForTool(string toolName);

        /// <summary>
        /// Shutdown all running MCP servers.
        /// </summary>
        Task ShutdownAllAsync();

        /// <summary>
        /// Update an existing MCP server configuration with a patch action.
        /// The server will be reconnected if it was running.
        /// </summary>
        Task UpdateServerConfigAsync(string serverName, Action<McpServerConfig> patchAction);
    }
}
