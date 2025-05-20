using Garnet.cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Vector
{
    public class QdrantProcessManager : BackgroundService, IService {
        public string ServiceName => "QdrantProcessManager";
        public ILogger<QdrantProcessManager> logger { get; private set; }
        public IHttpClientFactory HttpClientFactory { get; private set; }
        public HttpClient _httpClient { get; private set; }

        public string QdrantBinaryPath { get; set; } = Path.Combine(Env.WorkDir, "bin", "qdrant");
        public string QdrantDataPath { get; set; } = Path.Combine(Env.WorkDir, "qdrant_data");

        public int QdrantPort { get; private set; } = 6334;

        private Process _qdrantProcess;
        private bool _isRunning = false;
        public QdrantProcessManager(
            ILogger<QdrantProcessManager> logger,
            IHttpClientFactory HttpClientFactory) {
            this.logger = logger;
            this.HttpClientFactory = HttpClientFactory;
            _httpClient = HttpClientFactory.CreateClient();
        }

        public async Task DownloadQdrantBinaryAsync() {
            if (File.Exists(QdrantBinaryPath)) {
                return;
            }

            var downloadUrl = GetDownloadUrlForCurrentPlatform();
            var response = await _httpClient.GetAsync(downloadUrl);

            if (!response.IsSuccessStatusCode) {
                throw new Exception($"Failed to download Qdrant binary: {response.StatusCode}");
            }

            await using var fileStream = new FileStream(QdrantBinaryPath, FileMode.Create);
            await response.Content.CopyToAsync(fileStream);
        }

        private string GetDownloadUrlForCurrentPlatform() {
            if (OperatingSystem.IsWindows()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.7.4/qdrant-1.7.4-x86_64-pc-windows-msvc.zip";
            } else if (OperatingSystem.IsLinux()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.7.4/qdrant-1.7.4-x86_64-unknown-linux-gnu.tar.gz";
            } else if (OperatingSystem.IsMacOS()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.7.4/qdrant-1.7.4-x86_64-apple-darwin.tar.gz";
            }

            throw new PlatformNotSupportedException("Unsupported operating system");
        }

        public async Task StartQdrantAsync()
        {
            if (_isRunning) return;

            if (!File.Exists(QdrantBinaryPath))
            {
                await DownloadQdrantBinaryAsync();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = QdrantBinaryPath,
                Arguments = $"--data-dir {QdrantDataPath} --http-port {QdrantPort}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _qdrantProcess = new Process { StartInfo = startInfo };
            _qdrantProcess.EnableRaisingEvents = true;
            _qdrantProcess.Exited += (sender, args) => 
            {
                _isRunning = false;
                logger.LogWarning("Qdrant process exited with code: {ExitCode}", _qdrantProcess.ExitCode);
            };

            if (!_qdrantProcess.Start())
            {
                throw new Exception("Failed to start Qdrant process");
            }

            _isRunning = true;
            logger.LogInformation("Qdrant started on port {Port}", QdrantPort);
        }

        public async Task StopQdrantAsync()
        {
            if (!_isRunning) return;

            try
            {
                if (!_qdrantProcess.HasExited)
                {
                    _qdrantProcess.Kill();
                    await _qdrantProcess.WaitForExitAsync();
                }
            }
            finally
            {
                _isRunning = false;
                _qdrantProcess.Dispose();
                _qdrantProcess = null;
            }
        }

        public bool IsQdrantRunning() => _isRunning;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await StartQdrantAsync();
        }
    }
}