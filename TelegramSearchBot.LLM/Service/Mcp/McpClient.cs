using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TelegramSearchBot.Interface.Mcp;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Service.Mcp {
    /// <summary>
    /// MCP (Model Context Protocol) client that communicates with an MCP server via stdio.
    /// Implements the JSON-RPC 2.0 based MCP protocol specification.
    /// </summary>
    public class McpClient : IMcpClient {
        private readonly McpServerConfig _config;
        private readonly ILogger _logger;
        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private int _nextId = 1;
        private bool _disposed;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private const int ProcessExitTimeoutMs = 3000;

        public string ServerName => _config.Name;
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Returns true if the underlying process is still running.
        /// If the process has exited, sets IsConnected to false.
        /// </summary>
        public bool IsProcessAlive {
            get {
                if (_process == null) {
                    IsConnected = false;
                    return false;
                }
                try {
                    if (_process.HasExited) {
                        _logger.LogWarning("MCP server '{ServerName}' process (PID: {Pid}) has exited with code {ExitCode}.",
                            ServerName, _process.Id, _process.ExitCode);
                        IsConnected = false;
                        return false;
                    }
                    return true;
                } catch (InvalidOperationException) {
                    // Process object is in an invalid state
                    IsConnected = false;
                    return false;
                }
            }
        }

        public McpClient(McpServerConfig config, ILogger logger) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default) {
            // If already connected and process is alive, skip
            if (IsConnected && IsProcessAlive) return;

            // Clean up any existing dead process before reconnecting
            await CleanupProcessAsync();

            try {
                var startInfo = new ProcessStartInfo {
                    FileName = _config.Command,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                foreach (var arg in _config.Args) {
                    startInfo.ArgumentList.Add(arg);
                }

                foreach (var env in _config.Env) {
                    startInfo.Environment[env.Key] = env.Value;
                }

                _process = Process.Start(startInfo);
                if (_process == null) {
                    throw new InvalidOperationException($"Failed to start MCP server process: {_config.Command}");
                }

                _stdin = _process.StandardInput;
                _stdout = _process.StandardOutput;

                // Start reading stderr in background for logging
                var capturedProcess = _process;
                _ = Task.Run(async () => {
                    try {
                        while (!capturedProcess.HasExited) {
                            var line = await capturedProcess.StandardError.ReadLineAsync();
                            if (line != null) {
                                _logger.LogDebug("[MCP:{ServerName}:stderr] {Line}", ServerName, line);
                            }
                        }
                    } catch { /* Process exited */ }
                }, CancellationToken.None);

                // Send initialize request
                var initResult = await SendRequestAsync<McpInitializeResult>("initialize", new {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new {
                        name = "TelegramSearchBot",
                        version = "1.0.0"
                    }
                }, cancellationToken);

                _logger.LogInformation("MCP server '{ServerName}' initialized. Protocol: {Protocol}, Server: {ServerInfo}",
                    ServerName, initResult?.ProtocolVersion, initResult?.ServerInfo?.Name);

                // Send initialized notification
                await SendNotificationAsync("notifications/initialized", null, cancellationToken);

                IsConnected = true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to connect to MCP server '{ServerName}'", ServerName);
                await CleanupProcessAsync();
                throw;
            }
        }

        public async Task<List<McpToolDescription>> ListToolsAsync(CancellationToken cancellationToken = default) {
            if (!IsConnected || !IsProcessAlive) {
                throw new InvalidOperationException($"MCP client for '{ServerName}' is not connected.");
            }

            var result = await SendRequestAsync<McpToolsListResult>("tools/list", new { }, cancellationToken);
            return result?.Tools ?? new List<McpToolDescription>();
        }

        public async Task<McpToolCallResult> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default) {
            if (!IsConnected || !IsProcessAlive) {
                throw new InvalidOperationException($"MCP client for '{ServerName}' is not connected.");
            }

            var result = await SendRequestAsync<McpToolCallResult>("tools/call", new {
                name = toolName,
                arguments = arguments ?? new Dictionary<string, object>()
            }, cancellationToken);

            return result ?? new McpToolCallResult {
                Content = new List<McpContent> { new McpContent { Type = "text", Text = "No response from MCP server." } },
                IsError = true
            };
        }

        public async Task DisconnectAsync() {
            await CleanupProcessAsync();
        }

        /// <summary>
        /// Thoroughly cleans up the child process and associated streams.
        /// Safe to call multiple times.
        /// </summary>
        private async Task CleanupProcessAsync() {
            IsConnected = false;

            try {
                if (_stdin != null) {
                    await _stdin.DisposeAsync();
                    _stdin = null;
                }
            } catch { }

            try {
                if (_process != null && !_process.HasExited) {
                    _process.Kill(true);
                    // Wait briefly for the process to actually exit
                    try {
                    _process.WaitForExit(ProcessExitTimeoutMs);
                    } catch { }
                }
            } catch { }

            try {
                _process?.Dispose();
                _process = null;
            } catch { }
        }

        private async Task<T> SendRequestAsync<T>(string method, object parameters, CancellationToken cancellationToken) where T : class {
            await _sendLock.WaitAsync(cancellationToken);
            try {
                var id = _nextId++;
                var request = new JsonRpcRequest {
                    Id = id,
                    Method = method,
                    Params = parameters
                };

                var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings {
                    NullValueHandling = NullValueHandling.Ignore
                });

                _logger.LogDebug("[MCP:{ServerName}] Sending: {Json}", ServerName, json);
                await _stdin.WriteLineAsync(json);
                await _stdin.FlushAsync();

                // Read response with timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                while (!cts.Token.IsCancellationRequested) {
                    var responseLine = await ReadLineAsync(cts.Token);
                    if (string.IsNullOrWhiteSpace(responseLine)) continue;

                    _logger.LogDebug("[MCP:{ServerName}] Received: {Json}", ServerName, responseLine);

                    try {
                        var response = JsonConvert.DeserializeObject<JsonRpcResponse>(responseLine);
                        if (response?.Id == id) {
                            if (response.Error != null) {
                                throw new Exception($"MCP error ({response.Error.Code}): {response.Error.Message}");
                            }
                            if (response.Result == null) return null;
                            return response.Result.ToObject<T>();
                        }
                        // Not our response (could be a notification), continue reading
                    } catch (JsonException) {
                        // Not valid JSON-RPC, skip
                        _logger.LogWarning("[MCP:{ServerName}] Received non-JSON-RPC message: {Line}", ServerName, responseLine);
                    }
                }

                throw new TimeoutException($"Timeout waiting for response from MCP server '{ServerName}'");
            } finally {
                _sendLock.Release();
            }
        }

        private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) {
            await _sendLock.WaitAsync(cancellationToken);
            try {
                var notification = new JsonRpcRequest {
                    Id = null, // Notifications have no id
                    Method = method,
                    Params = parameters
                };

                var json = JsonConvert.SerializeObject(notification, new JsonSerializerSettings {
                    NullValueHandling = NullValueHandling.Ignore
                });

                _logger.LogDebug("[MCP:{ServerName}] Sending notification: {Json}", ServerName, json);
                await _stdin.WriteLineAsync(json);
                await _stdin.FlushAsync();
            } finally {
                _sendLock.Release();
            }
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken) {
            var taskCompletionSource = new TaskCompletionSource<string>();

            using var registration = cancellationToken.Register(() => taskCompletionSource.TrySetCanceled());

            var readTask = _stdout.ReadLineAsync();
            var completedTask = await Task.WhenAny(readTask, taskCompletionSource.Task);

            if (completedTask == taskCompletionSource.Task) {
                throw new OperationCanceledException(cancellationToken);
            }

            return await readTask;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            IsConnected = false;
            try {
                _stdin?.Dispose();
                _stdin = null;
            } catch { }
            try {
                if (_process != null && !_process.HasExited) {
                    _process.Kill(true);
                    try { _process.WaitForExit(ProcessExitTimeoutMs); } catch { }
                }
                _process?.Dispose();
                _process = null;
            } catch { }
            _sendLock.Dispose();
        }
    }
}
