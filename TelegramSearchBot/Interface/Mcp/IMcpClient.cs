using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Interface.Mcp {
    /// <summary>
    /// Interface for an MCP (Model Context Protocol) client that connects to an external MCP server via stdio.
    /// </summary>
    public interface IMcpClient : IDisposable {
        /// <summary>
        /// The name of the MCP server this client is connected to.
        /// </summary>
        string ServerName { get; }

        /// <summary>
        /// Whether the client is currently connected and initialized.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connect to the MCP server and perform the initialize handshake.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// List all available tools from the MCP server.
        /// </summary>
        Task<List<McpToolDescription>> ListToolsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Call a tool on the MCP server.
        /// </summary>
        Task<McpToolCallResult> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnect from the MCP server.
        /// </summary>
        Task DisconnectAsync();
    }
}
