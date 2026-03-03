using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Service.Tools {
    /// <summary>
    /// Built-in tool for managing MCP (Model Context Protocol) tool servers.
    /// Allows the LLM to install, configure, and manage external MCP tool servers.
    /// Restricted to admin users for security.
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class McpInstallerToolService : IService, IMcpInstallerToolService {
        private readonly ILogger<McpInstallerToolService> _logger;
        private readonly IMcpServerManager _mcpServerManager;

        public string ServiceName => "McpInstallerToolService";

        public McpInstallerToolService(
            ILogger<McpInstallerToolService> logger,
            IMcpServerManager mcpServerManager) {
            _logger = logger;
            _mcpServerManager = mcpServerManager;
        }

        [BuiltInTool("List all configured MCP (Model Context Protocol) tool servers and their status. Shows server name, command, and available tools.")]
        public async Task<string> ListMcpServers() {
            try {
                var configs = _mcpServerManager.GetServerConfigs();
                var externalTools = _mcpServerManager.GetAllExternalTools();

                if (!configs.Any()) {
                    return "No MCP servers configured.\n\n" +
                        "To add an MCP server, use the AddMcpServer tool with:\n" +
                        "- name: A unique name for the server\n" +
                        "- command: The command to start the server (e.g., 'npx', 'uvx', 'node')\n" +
                        "- args: Space-separated arguments (e.g., '-y @modelcontextprotocol/server-filesystem /tmp')\n" +
                        "- env: Optional environment variables in KEY=VALUE format, semicolon-separated";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Configured MCP Servers ({configs.Count}):");
                sb.AppendLine("---");

                foreach (var config in configs) {
                    sb.AppendLine($"Server: {config.Name}");
                    sb.AppendLine($"  Command: {config.Command} {string.Join(" ", config.Args)}");
                    sb.AppendLine($"  Enabled: {config.Enabled}");

                    var serverTools = externalTools.Where(t => t.serverName == config.Name).ToList();
                    if (serverTools.Any()) {
                        sb.AppendLine($"  Tools ({serverTools.Count}):");
                        foreach (var (_, tool) in serverTools) {
                            sb.AppendLine($"    - {tool.Name}: {tool.Description}");
                        }
                    } else {
                        sb.AppendLine("  Tools: Not connected or no tools available");
                    }
                    sb.AppendLine();
                }

                return await Task.FromResult(sb.ToString());
            } catch (Exception ex) {
                _logger.LogError(ex, "Error listing MCP servers");
                return $"Error listing MCP servers: {ex.Message}";
            }
        }

        [BuiltInTool(@"Add and configure a new MCP (Model Context Protocol) tool server. 
The server will be started automatically and its tools will be available for use.
Common MCP servers:
- File system: command='npx', args='-y @modelcontextprotocol/server-filesystem /path'
- GitHub: command='npx', args='-y @modelcontextprotocol/server-github'
- Brave Search: command='npx', args='-y @modelcontextprotocol/server-brave-search'
Only available to admin users.")]
        public async Task<string> AddMcpServer(
            [BuiltInParameter("A unique name for the MCP server (e.g., 'filesystem', 'github')")] string name,
            [BuiltInParameter("The command to start the MCP server (e.g., 'npx', 'uvx', 'node', 'python')")] string command,
            [BuiltInParameter("Space-separated command arguments (e.g., '-y @modelcontextprotocol/server-filesystem /tmp')")] string args,
            ToolContext toolContext,
            [BuiltInParameter("Optional environment variables in KEY=VALUE format, separated by semicolons (e.g., 'GITHUB_TOKEN=abc;API_KEY=xyz')", IsRequired = false)] string env = null) {

            // Security check: only allow admin users
            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: MCP server management is only available to admin users.";
            }

            try {
                if (string.IsNullOrWhiteSpace(name)) {
                    return "Error: Server name is required.";
                }
                if (string.IsNullOrWhiteSpace(command)) {
                    return "Error: Command is required.";
                }

                var config = new McpServerConfig {
                    Name = name.Trim(),
                    Command = command.Trim(),
                    Args = string.IsNullOrWhiteSpace(args)
                        ? new List<string>()
                        : args.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Enabled = true,
                };

                // Parse environment variables
                if (!string.IsNullOrWhiteSpace(env)) {
                    foreach (var pair in env.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                        var parts = pair.Split('=', 2);
                        if (parts.Length == 2) {
                            config.Env[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }

                await _mcpServerManager.AddServerAsync(config);

                // Check if tools were discovered
                var tools = _mcpServerManager.GetAllExternalTools()
                    .Where(t => t.serverName == name)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"Successfully added MCP server '{name}'.");

                if (tools.Any()) {
                    sb.AppendLine($"Discovered {tools.Count} tools:");
                    foreach (var (_, tool) in tools) {
                        sb.AppendLine($"  - mcp_{name}_{tool.Name}: {tool.Description}");
                    }
                } else {
                    sb.AppendLine("Note: No tools were discovered. The server may have failed to connect.");
                    sb.AppendLine("Use the bash tool to check if the required software is installed.");
                }

                return sb.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error adding MCP server: {Name}", name);
                return $"Error adding MCP server '{name}': {ex.Message}\n" +
                    "Make sure the command is installed and accessible. You can use the bash tool to install prerequisites.";
            }
        }

        [BuiltInTool("Remove a configured MCP tool server by name. This will disconnect the server and remove its configuration. Only available to admin users.")]
        public async Task<string> RemoveMcpServer(
            [BuiltInParameter("The name of the MCP server to remove")] string name,
            ToolContext toolContext) {

            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: MCP server management is only available to admin users.";
            }

            try {
                if (string.IsNullOrWhiteSpace(name)) {
                    return "Error: Server name is required.";
                }

                var configs = _mcpServerManager.GetServerConfigs();
                if (!configs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                    return $"Error: MCP server '{name}' not found. Use ListMcpServers to see available servers.";
                }

                await _mcpServerManager.RemoveServerAsync(name);
                return $"Successfully removed MCP server '{name}'.";
            } catch (Exception ex) {
                _logger.LogError(ex, "Error removing MCP server: {Name}", name);
                return $"Error removing MCP server '{name}': {ex.Message}";
            }
        }

        [BuiltInTool("Restart all enabled MCP tool servers. Use this after making configuration changes or if tools are not responding. Only available to admin users.")]
        public async Task<string> RestartMcpServers(ToolContext toolContext) {
            if (toolContext == null || toolContext.UserId != Env.AdminId) {
                return "Error: MCP server management is only available to admin users.";
            }

            try {
                await _mcpServerManager.ShutdownAllAsync();
                await _mcpServerManager.InitializeAllServersAsync();

                var tools = _mcpServerManager.GetAllExternalTools();
                return $"Successfully restarted MCP servers. {tools.Count} external tools available.";
            } catch (Exception ex) {
                _logger.LogError(ex, "Error restarting MCP servers");
                return $"Error restarting MCP servers: {ex.Message}";
            }
        }
    }
}
