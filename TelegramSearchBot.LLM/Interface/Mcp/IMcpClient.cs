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
        /// Whether the underlying server process is still alive.
        /// Checking this will also update IsConnected if the process has exited.
        /// </summary>
        bool IsProcessAlive { get; }

        /// <summary>
        /// Connect to the MCP server and perform the initialize handshake.
        /// If the process previously died, this will clean up and start a fresh process.
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
        /// Send a lightweight heartbeat request to verify the connection is still responsive.
        /// Returns true if the server responds, false if it times out or fails.
        /// </summary>
        Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnect from the MCP server.
        /// </summary>
        Task DisconnectAsync();
    }
}
