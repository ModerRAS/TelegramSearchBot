using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model.Mcp;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.Manage {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class EditMcpConfService : IService {
        public string ServiceName => "EditMcpConfService";
        private readonly IMcpServerManager _mcpServerManager;
        private readonly ILogger<EditMcpConfService> _logger;
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }

        private readonly Dictionary<string, Func<EditMcpConfRedisHelper, string, Task<(bool, string)>>> _stateHandlers;

        private const string EnvPrefix = "env:";
        private const string FieldIdCommand = "1";
        private const string FieldIdArgs = "2";
        private const string FieldIdEnv = "3";
        private const string FieldIdTimeout = "4";
        private const string FieldIdEnabled = "5";

        // Field IDs for editing
        private readonly Dictionary<string, string> _fieldNames = new() {
            { FieldIdCommand, "命令(Command)" },
            { FieldIdArgs, "参数(Args)" },
            { FieldIdEnv, "环境变量(写入)" },
            { FieldIdTimeout, "超时时间(Timeout)" },
            { FieldIdEnabled, "启用/禁用(Enabled)" }
        };

        public EditMcpConfService(
            IMcpServerManager mcpServerManager,
            ILogger<EditMcpConfService> logger,
            IConnectionMultiplexer connectionMultiplexer) {
            _mcpServerManager = mcpServerManager ?? throw new ArgumentNullException(nameof(mcpServerManager));
            _logger = logger;
            this.connectionMultiplexer = connectionMultiplexer;

            _stateHandlers = new Dictionary<string, Func<EditMcpConfRedisHelper, string, Task<(bool, string)>>>
            {
                { McpConfState.AddingName.GetDescription(), HandleAddingNameAsync },
                { McpConfState.AddingCommand.GetDescription(), HandleAddingCommandAsync },
                { McpConfState.AddingArgs.GetDescription(), HandleAddingArgsAsync },
                { McpConfState.AddingEnvKey.GetDescription(), HandleAddingEnvKeyAsync },
                { McpConfState.AddingEnvValue.GetDescription(), HandleAddingEnvValueAsync },
                { McpConfState.AddingTimeout.GetDescription(), HandleAddingTimeoutAsync },
                { McpConfState.EditingSelectServer.GetDescription(), HandleEditingSelectServerAsync },
                { McpConfState.EditingSelectField.GetDescription(), HandleEditingSelectFieldAsync },
                { McpConfState.EditingInputValue.GetDescription(), HandleEditingInputValueAsync },
                { McpConfState.EditingEnvKey.GetDescription(), HandleEditingEnvKeyAsync },
                { McpConfState.EditingEnvValue.GetDescription(), HandleEditingEnvValueAsync },
                { McpConfState.DeletingSelectServer.GetDescription(), HandleDeletingSelectServerAsync },
                { McpConfState.DeletingConfirm.GetDescription(), HandleDeletingConfirmAsync },
            };
        }

        public async Task<(bool, string)> ExecuteAsync(string command, long chatId) {
            var redis = new EditMcpConfRedisHelper(connectionMultiplexer, chatId);
            var currentState = await redis.GetStateAsync();

            // Handle direct commands
            var directResult = await HandleDirectCommandsAsync(redis, command);
            if (directResult.HasValue) {
                return directResult.Value;
            }

            // Handle state-based commands
            var stateCommandResult = await HandleStateBasedCommandsAsync(redis, command);
            if (stateCommandResult.HasValue) {
                return stateCommandResult.Value;
            }

            // Process input based on current state
            if (string.IsNullOrEmpty(currentState)) {
                return (false, "");
            }

            if (_stateHandlers.TryGetValue(currentState, out var handler)) {
                return await handler(redis, command);
            }

            return (false, "");
        }

        private async Task<(bool, string)?> HandleDirectCommandsAsync(EditMcpConfRedisHelper redis, string command) {
            var cmd = command.Trim();

            if (cmd.Equals("查看MCP服务器", StringComparison.OrdinalIgnoreCase) || cmd.Equals("查看mcp服务器", StringComparison.OrdinalIgnoreCase)) {
                return await ListServersAsync();
            }

            if (cmd.Equals("重启MCP服务器", StringComparison.OrdinalIgnoreCase) || cmd.Equals("重启mcp服务器", StringComparison.OrdinalIgnoreCase)) {
                return await RestartAllServersAsync();
            }

            return null;
        }

        private async Task<(bool, string)?> HandleStateBasedCommandsAsync(EditMcpConfRedisHelper redis, string command) {
            var cmd = command.Trim();

            if (cmd.Equals("新建MCP服务器", StringComparison.OrdinalIgnoreCase) || cmd.Equals("新建mcp服务器", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(McpConfState.AddingName.GetDescription());
                await redis.SetDataAsync("");
                return (true, "请输入MCP服务器的唯一名称（例如：minimax, github, filesystem）：");
            }

            if (cmd.Equals("编辑MCP服务器", StringComparison.OrdinalIgnoreCase) || cmd.Equals("编辑mcp服务器", StringComparison.OrdinalIgnoreCase)) {
                return await HandleEditServerCommandAsync(redis);
            }

            if (cmd.Equals("删除MCP服务器", StringComparison.OrdinalIgnoreCase) || cmd.Equals("删除mcp服务器", StringComparison.OrdinalIgnoreCase)) {
                return await HandleDeleteServerCommandAsync(redis);
            }

            return null;
        }

        // ============================================================
        //  Add server flow: Name → Command → Args → Env(loop) → Timeout → Create
        // ============================================================

        private async Task<(bool, string)> HandleAddingNameAsync(EditMcpConfRedisHelper redis, string command) {
            var name = command.Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                return (true, "名称不能为空，请重新输入MCP服务器的名称：");
            }

            // Check if name already exists
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            if (configs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                return (true, $"名称 '{name}' 已存在，请输入其他名称：");
            }

            await redis.SetDataAsync(name);
            await redis.SetStateAsync(McpConfState.AddingCommand.GetDescription());
            return (true, "请输入启动命令（例如：npx, uvx, node, python）：");
        }

        private async Task<(bool, string)> HandleAddingCommandAsync(EditMcpConfRedisHelper redis, string command) {
            var cmd = command.Trim();
            if (string.IsNullOrWhiteSpace(cmd)) {
                return (true, "命令不能为空，请重新输入启动命令：");
            }

            var data = await redis.GetDataAsync();
            await redis.SetDataAsync($"{data}|{cmd}");
            await redis.SetStateAsync(McpConfState.AddingArgs.GetDescription());
            return (true, "请输入命令参数（空格分隔，例如：-y @modelcontextprotocol/server-filesystem /tmp），输入 - 跳过：");
        }

        private async Task<(bool, string)> HandleAddingArgsAsync(EditMcpConfRedisHelper redis, string command) {
            var args = command.Trim();
            if (args == "-") args = "";

            var data = await redis.GetDataAsync();
            await redis.SetDataAsync($"{data}|{args}");
            await redis.SetStateAsync(McpConfState.AddingEnvKey.GetDescription());
            return (true, "请输入环境变量名（例如：API_KEY），输入 完成 结束环境变量设置：");
        }

        private async Task<(bool, string)> HandleAddingEnvKeyAsync(EditMcpConfRedisHelper redis, string command) {
            var key = command.Trim();

            if (key.Equals("完成", StringComparison.OrdinalIgnoreCase)) {
                await redis.SetStateAsync(McpConfState.AddingTimeout.GetDescription());
                return (true, "请输入超时时间（秒，默认30）：");
            }

            if (string.IsNullOrWhiteSpace(key)) {
                return (true, "环境变量名不能为空，请重新输入，或输入 完成 结束：");
            }

            // Store env key temporarily - append with env: prefix
            var data = await redis.GetDataAsync();
            await redis.SetDataAsync($"{data}|{EnvPrefix}{key}");
            await redis.SetStateAsync(McpConfState.AddingEnvValue.GetDescription());
            return (true, $"请输入环境变量 {key} 的值：");
        }

        private async Task<(bool, string)> HandleAddingEnvValueAsync(EditMcpConfRedisHelper redis, string command) {
            var value = command.Trim();
            var data = await redis.GetDataAsync();

            // The data ends with |env:KEY, append =VALUE
            await redis.SetDataAsync($"{data}={value}");
            await redis.SetStateAsync(McpConfState.AddingEnvKey.GetDescription());
            return (true, "环境变量已添加。请继续输入下一个环境变量名，或输入 完成 结束：");
        }

        private async Task<(bool, string)> HandleAddingTimeoutAsync(EditMcpConfRedisHelper redis, string command) {
            var input = command.Trim();
            int timeout = 30;
            if (!string.IsNullOrWhiteSpace(input) && input != "-") {
                if (!int.TryParse(input, out timeout) || timeout <= 0) {
                    return (true, "超时时间必须为正整数，请重新输入：");
                }
            }

            var data = await redis.GetDataAsync();
            // Parse accumulated data: name|command|args|env:KEY1=VAL1|env:KEY2=VAL2|...
            var parts = data.Split('|');
            if (parts.Length < 3) {
                await redis.DeleteKeysAsync();
                return (true, "数据格式错误，请重新开始。发送 新建MCP服务器 重试。");
            }

            var name = parts[0];
            var cmd = parts[1];
            var args = parts[2];

            var config = new McpServerConfig {
                Name = name,
                Command = cmd,
                Args = string.IsNullOrWhiteSpace(args)
                    ? new List<string>()
                    : args.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                Enabled = true,
                TimeoutSeconds = timeout,
            };

            // Parse env vars
            for (int i = 3; i < parts.Length; i++) {
                if (parts[i].StartsWith(EnvPrefix)) {
                    var envPair = parts[i][EnvPrefix.Length..];
                    var eqIdx = envPair.IndexOf('=');
                    if (eqIdx > 0) {
                        config.Env[envPair[..eqIdx]] = envPair[( eqIdx + 1 )..];
                    }
                }
            }

            try {
                await _mcpServerManager.AddServerAsync(config);
                McpToolHelper.RegisterExternalMcpTools(_mcpServerManager);

                var tools = _mcpServerManager.GetAllExternalTools()
                    .Where(t => t.serverName == name).ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"✅ MCP服务器 '{name}' 创建成功！");
                sb.AppendLine($"  命令: {cmd} {args}");
                sb.AppendLine($"  超时: {timeout}s");
                if (config.Env.Count > 0) {
                    sb.AppendLine($"  环境变量: {string.Join(", ", config.Env.Keys)}");
                }
                if (tools.Any()) {
                    sb.AppendLine($"  发现 {tools.Count} 个工具:");
                    foreach (var (_, tool) in tools) {
                        sb.AppendLine($"    - {tool.Name}: {tool.Description}");
                    }
                } else {
                    sb.AppendLine("  注意: 暂未发现工具，服务器可能连接失败。");
                }

                await redis.DeleteKeysAsync();
                return (true, sb.ToString());
            } catch (Exception ex) {
                _logger.LogError(ex, "Error creating MCP server: {Name}", name);
                await redis.DeleteKeysAsync();
                return (true, $"❌ 创建MCP服务器 '{name}' 失败: {ex.Message}");
            }
        }

        // ============================================================
        //  Edit server flow: Select server → Select field → Input value
        // ============================================================

        private async Task<(bool, string)> HandleEditServerCommandAsync(EditMcpConfRedisHelper redis) {
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            if (!configs.Any()) {
                return (true, "当前没有已配置的MCP服务器。发送 新建MCP服务器 添加一个。");
            }

            var sb = new StringBuilder();
            sb.AppendLine("请选择要编辑的MCP服务器（输入名称）：");
            foreach (var config in configs) {
                var status = config.Enabled ? "✅" : "❌";
                sb.AppendLine($"  {status} {config.Name} ({config.Command} {string.Join(" ", config.Args)})");
            }

            await redis.SetStateAsync(McpConfState.EditingSelectServer.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleEditingSelectServerAsync(EditMcpConfRedisHelper redis, string command) {
            var serverName = command.Trim();
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (config == null) {
                return (true, $"未找到MCP服务器 '{serverName}'，请重新输入名称：");
            }

            await redis.SetDataAsync(config.Name);
            await redis.SetStateAsync(McpConfState.EditingSelectField.GetDescription());
            return (true, BuildFieldSelectionPrompt(config));
        }

        private string BuildFieldSelectionPrompt(McpServerConfig config) {
            var sb = new StringBuilder();
            sb.AppendLine($"正在编辑MCP服务器: {config.Name}");
            sb.AppendLine("请选择要编辑的字段（输入编号）：");
            sb.AppendLine($"  1. 命令(Command): {config.Command}");
            sb.AppendLine($"  2. 参数(Args): {string.Join(" ", config.Args)}");
            sb.AppendLine($"  3. 环境变量(写入) - 当前有 {config.Env.Count} 个变量: {string.Join(", ", config.Env.Keys)}");
            sb.AppendLine($"  4. 超时时间(Timeout): {config.TimeoutSeconds}s");
            sb.AppendLine($"  5. 启用/禁用(Enabled): {config.Enabled}");
            return sb.ToString();
        }

        private async Task<(bool, string)> HandleEditingSelectFieldAsync(EditMcpConfRedisHelper redis, string command) {
            var field = command.Trim();
            if (!_fieldNames.ContainsKey(field)) {
                return (true, "无效的选项，请输入 1-5 的数字：");
            }

            var data = await redis.GetDataAsync();

            if (field == FieldIdEnv) {
                // Env var editing: go to env key input
                await redis.SetDataAsync($"{data}|3");
                await redis.SetStateAsync(McpConfState.EditingEnvKey.GetDescription());
                return (true, "请输入要设置的环境变量名（注意：这是写入操作，只会添加/覆盖指定的变量，不会影响其他变量）：");
            }

            await redis.SetDataAsync($"{data}|{field}");
            await redis.SetStateAsync(McpConfState.EditingInputValue.GetDescription());

            return field switch {
                FieldIdCommand => (true, "请输入新的启动命令："),
                FieldIdArgs => (true, "请输入新的命令参数（空格分隔）："),
                FieldIdTimeout => (true, "请输入新的超时时间（秒）："),
                FieldIdEnabled => (true, "请输入新的状态（true=启用, false=禁用）："),
                _ => (true, "请输入新的值：")
            };
        }

        private async Task<(bool, string)> HandleEditingInputValueAsync(EditMcpConfRedisHelper redis, string command) {
            var value = command.Trim();
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            if (parts.Length < 2) {
                await redis.DeleteKeysAsync();
                return (true, "数据格式错误，请重新开始。");
            }

            var serverName = parts[0];
            var field = parts[1];

            try {
                string changeDescription = "";
                await _mcpServerManager.UpdateServerConfigAsync(serverName, config => {
                    switch (field) {
                        case FieldIdCommand:
                            config.Command = value;
                            changeDescription = $"命令已更新为: {value}";
                            break;
                        case FieldIdArgs:
                            config.Args = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                            changeDescription = $"参数已更新为: {value}";
                            break;
                        case FieldIdTimeout:
                            if (int.TryParse(value, out var timeout) && timeout > 0) {
                                config.TimeoutSeconds = timeout;
                                changeDescription = $"超时时间已更新为: {timeout}s";
                            } else {
                                changeDescription = "❌ 超时时间必须为正整数";
                            }
                            break;
                        case FieldIdEnabled:
                            if (bool.TryParse(value, out var enabled)) {
                                config.Enabled = enabled;
                                changeDescription = $"状态已更新为: {( enabled ? "启用" : "禁用" )}";
                            } else {
                                changeDescription = "❌ 请输入 true 或 false";
                            }
                            break;
                    }
                });

                McpToolHelper.RegisterExternalMcpTools(_mcpServerManager);
                await redis.DeleteKeysAsync();
                return (true, $"✅ MCP服务器 '{serverName}' {changeDescription}");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error updating MCP server: {Name}", serverName);
                await redis.DeleteKeysAsync();
                return (true, $"❌ 更新失败: {ex.Message}");
            }
        }

        private async Task<(bool, string)> HandleEditingEnvKeyAsync(EditMcpConfRedisHelper redis, string command) {
            var key = command.Trim();
            if (string.IsNullOrWhiteSpace(key)) {
                return (true, "环境变量名不能为空，请重新输入：");
            }

            var data = await redis.GetDataAsync();
            // data = "serverName|3", append env key
            await redis.SetDataAsync($"{data}|{key}");
            await redis.SetStateAsync(McpConfState.EditingEnvValue.GetDescription());
            return (true, $"请输入环境变量 {key} 的值：");
        }

        private async Task<(bool, string)> HandleEditingEnvValueAsync(EditMcpConfRedisHelper redis, string command) {
            var value = command.Trim();
            var data = await redis.GetDataAsync();
            var parts = data.Split('|');
            // parts = ["serverName", "3", "envKey"]
            if (parts.Length < 3) {
                await redis.DeleteKeysAsync();
                return (true, "数据格式错误，请重新开始。");
            }

            var serverName = parts[0];
            var envKey = parts[2];

            try {
                await _mcpServerManager.UpdateServerConfigAsync(serverName, config => {
                    config.Env[envKey] = value;
                });

                McpToolHelper.RegisterExternalMcpTools(_mcpServerManager);
                await redis.DeleteKeysAsync();
                return (true, $"✅ MCP服务器 '{serverName}' 环境变量 '{envKey}' 已设置。");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error updating MCP server env: {Name}", serverName);
                await redis.DeleteKeysAsync();
                return (true, $"❌ 设置环境变量失败: {ex.Message}");
            }
        }

        // ============================================================
        //  Delete server flow: Select server → Confirm → Delete
        // ============================================================

        private async Task<(bool, string)> HandleDeleteServerCommandAsync(EditMcpConfRedisHelper redis) {
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            if (!configs.Any()) {
                return (true, "当前没有已配置的MCP服务器。");
            }

            var sb = new StringBuilder();
            sb.AppendLine("请选择要删除的MCP服务器（输入名称）：");
            foreach (var config in configs) {
                sb.AppendLine($"  - {config.Name} ({config.Command})");
            }

            await redis.SetStateAsync(McpConfState.DeletingSelectServer.GetDescription());
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> HandleDeletingSelectServerAsync(EditMcpConfRedisHelper redis, string command) {
            var serverName = command.Trim();
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var config = configs.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (config == null) {
                return (true, $"未找到MCP服务器 '{serverName}'，请重新输入名称：");
            }

            await redis.SetDataAsync(config.Name);
            await redis.SetStateAsync(McpConfState.DeletingConfirm.GetDescription());
            return (true, $"确认删除MCP服务器 '{config.Name}'？输入 是 确认，输入其他取消：");
        }

        private async Task<(bool, string)> HandleDeletingConfirmAsync(EditMcpConfRedisHelper redis, string command) {
            var confirm = command.Trim();
            var serverName = await redis.GetDataAsync();

            if (confirm.Equals("是", StringComparison.OrdinalIgnoreCase)) {
                try {
                    await _mcpServerManager.RemoveServerAsync(serverName);
                    McpToolHelper.RegisterExternalMcpTools(_mcpServerManager);
                    await redis.DeleteKeysAsync();
                    return (true, $"✅ MCP服务器 '{serverName}' 已删除。");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error removing MCP server: {Name}", serverName);
                    await redis.DeleteKeysAsync();
                    return (true, $"❌ 删除MCP服务器失败: {ex.Message}");
                }
            } else {
                await redis.DeleteKeysAsync();
                return (true, "已取消删除操作。");
            }
        }

        // ============================================================
        //  Direct command handlers
        // ============================================================

        private async Task<(bool, string)> ListServersAsync() {
            var configs = await _mcpServerManager.GetServerConfigsAsync();
            var externalTools = _mcpServerManager.GetAllExternalTools();

            if (!configs.Any()) {
                return (true, "当前没有已配置的MCP服务器。\n\n" +
                    "可用命令:\n" +
                    "  新建MCP服务器 - 添加新的MCP服务器\n" +
                    "  编辑MCP服务器 - 编辑已有服务器配置\n" +
                    "  删除MCP服务器 - 删除服务器\n" +
                    "  重启MCP服务器 - 重启所有服务器");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"已配置的MCP服务器 ({configs.Count} 个):");
            sb.AppendLine("---");

            foreach (var config in configs) {
                var status = config.Enabled ? "✅" : "❌";
                sb.AppendLine($"{status} {config.Name}");
                sb.AppendLine($"  命令: {config.Command} {string.Join(" ", config.Args)}");
                sb.AppendLine($"  超时: {config.TimeoutSeconds}s");
                if (config.Env.Count > 0) {
                    sb.AppendLine($"  环境变量: {string.Join(", ", config.Env.Keys)}");
                }

                var serverTools = externalTools.Where(t => t.serverName == config.Name).ToList();
                if (serverTools.Any()) {
                    sb.AppendLine($"  工具 ({serverTools.Count} 个):");
                    foreach (var (_, tool) in serverTools) {
                        sb.AppendLine($"    - {tool.Name}: {tool.Description}");
                    }
                } else {
                    sb.AppendLine("  工具: 未连接或无可用工具");
                }
                sb.AppendLine();
            }

            sb.AppendLine("可用命令: 新建MCP服务器 | 编辑MCP服务器 | 删除MCP服务器 | 重启MCP服务器");
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> RestartAllServersAsync() {
            try {
                await _mcpServerManager.ShutdownAllAsync();
                await _mcpServerManager.InitializeAllServersAsync();
                McpToolHelper.RegisterExternalMcpTools(_mcpServerManager);

                var tools = _mcpServerManager.GetAllExternalTools();
                return (true, $"✅ MCP服务器已重启。共 {tools.Count} 个工具可用。");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error restarting MCP servers");
                return (true, $"❌ 重启MCP服务器失败: {ex.Message}");
            }
        }
    }
}
