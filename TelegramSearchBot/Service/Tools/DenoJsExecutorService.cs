using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using J2N.IO;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class DenoJsExecutorService : IService, IDenoJsExecutorService {
        private readonly IHttpClientFactory _httpClientFactory;
        internal string _denoPath;
        internal string _denoDir;

        public string ServiceName => "DenoJsExecutorService";

        public DenoJsExecutorService(IHttpClientFactory httpClientFactory) {
            _httpClientFactory = httpClientFactory;
            _denoDir = Path.Combine(Env.WorkDir, "bin");
            _denoPath = Path.Combine(_denoDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "deno.exe" : "deno");

            if (!Directory.Exists(_denoDir)) {
                Directory.CreateDirectory(_denoDir);
            }
        }

        private async Task EnsureDenoInstalledAsync() {
            if (File.Exists(_denoPath)) return;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            var downloadUrl = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip"
                : "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-unknown-linux-gnu.zip";

            var tempDir = Path.Combine(Env.WorkDir, "temp");
            if (!Directory.Exists(tempDir)) {
                Directory.CreateDirectory(tempDir);
            }
            var tempFile = Path.Combine(tempDir, $"deno-download-{Guid.NewGuid()}.tmp");
            try {
                // 下载文件并验证大小
                var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength == null || contentLength < 1024 * 1024) // 小于1MB视为无效
                    throw new Exception("Invalid download size detected");

                await using (var fs = new FileStream(tempFile, FileMode.Create)) {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var readStream = await response.Content.ReadAsStreamAsync();

                    while (true) {
                        var read = await readStream.ReadAsync(buffer);
                        if (read == 0) break;

                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;

                        // 检查下载是否中断
                        if (contentLength.HasValue && totalRead > contentLength)
                            throw new Exception("Download corrupted - size mismatch");
                    }

                    // 验证下载完整性
                    if (contentLength.HasValue && totalRead != contentLength)
                        throw new Exception("Download incomplete");
                }


                // 验证ZIP文件有效性
                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempFile)) {
                    if (archive.Entries.Count == 0)
                        throw new Exception("Invalid ZIP file - no entries");
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, _denoDir);
            } catch (Exception ex) {
                // 清理可能损坏的文件
                try {
                    if (File.Exists(tempFile)) {
                        await Task.Delay(100); // Wait for handles to release
                        File.Delete(tempFile);
                    }
                    if (Directory.Exists(_denoDir)) {
                        await Task.Delay(100);
                        Directory.Delete(_denoDir, true);
                    }
                } catch { }

                throw new Exception($"Failed to install Deno: {ex.Message}", ex);
            } finally {
                try {
                    if (File.Exists(tempFile)) {
                        await Task.Delay(100);
                        File.Delete(tempFile);
                    }
                } catch { }
            }
        }

        [McpTool(@"
## Deno JavaScript 执行器 [重要：Deno环境]

⚠️ 特别注意：这是在Deno运行时环境执行，不是Node.js也不是浏览器环境！

### 环境差异说明：
1. Deno vs Node.js:
   - 无require()，使用ES模块import/export
   - 标准库URL格式：`import * as fs from 'https://deno.land/std/fs/mod.ts'`
   - 严格权限控制，需要显式授权

2. Deno vs 浏览器:
   - 无DOM/BOM API (document, window等)
   - 无Web API (localStorage等)
   - 有完整的文件系统访问能力

### 功能说明：
安全执行JavaScript代码并返回console.log输出

### 输入要求：
- 纯JavaScript/TypeScript代码（ES6+语法）
- 使用ES模块语法(import/export)
- 通过console.log输出结果
- 标准库使用Deno官方库(deno.land/std)

### 使用示例：
```javascript
// 使用Deno标准库读取文件
import { readTextFile } from 'https://deno.land/std/fs/mod.ts';

const content = await readTextFile('data.txt');
console.log(content);
```

### 权限说明：
- 已启用： --allow-read=. --allow-write=. --allow-net --no-prompt
- 其他权限默认禁用

### 注意事项：
1. 代码执行有超时限制（默认5秒）
2. 禁止访问敏感系统资源
3. 临时文件会自动清理
4. 必须使用Deno兼容的API
")]
        public async Task<string> ExecuteJs(
            [McpParameter("要执行的JavaScript代码，支持ES6+语法，通过console.log输出结果")] string jsCode,
            [McpParameter("执行超时时间（毫秒），默认5000")] int timeoutMs = 5000) {
            var tempDir = Path.Combine(Env.WorkDir, "temp");
            if (!Directory.Exists(tempDir)) {
                Directory.CreateDirectory(tempDir);
            }
            var tempFile = Path.Combine(tempDir, $"deno-exec-{Guid.NewGuid()}.js");
            await File.WriteAllTextAsync(tempFile, jsCode);

            try {
                await EnsureDenoInstalledAsync();

                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _denoPath,
                        Arguments = $"run --allow-read=. --allow-write=. --allow-net --no-prompt {tempFile}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = tempDir
                    }
                };

                process.Start();

                using var cts = new CancellationTokenSource(timeoutMs);
                try {
                    await process.WaitForExitAsync(cts.Token);
                } catch (OperationCanceledException) {
                    string partialOutput = "";
                    try {
                        if (!process.HasExited) {
                            // Read any available output with timeout before killing
                            var outputTask = process.StandardOutput.ReadToEndAsync();
                            if (await Task.WhenAny(outputTask, Task.Delay(500)) == outputTask) {
                                partialOutput = await outputTask;
                            }
                            process.Kill(true);
                        }
                    } catch { }
                    throw new TimeoutException($"Deno execution timed out after {timeoutMs}ms. Partial output:\n{partialOutput}");
                }

                if (process.ExitCode != 0) {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"Deno execution failed: {error}");
                }

                return await process.StandardOutput.ReadToEndAsync();
            } finally {
                try {
                    if (File.Exists(tempFile)) {
                        await Task.Delay(100);
                        File.Delete(tempFile);
                    }
                } catch { }
            }
        }
    }
}
