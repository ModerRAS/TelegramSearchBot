using Garnet.cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scriban;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Manager {
    public class QdrantProcessManager : BackgroundService, IService {
        public string ServiceName => "QdrantProcessManager";
        public ILogger<QdrantProcessManager> logger { get; private set; }
        public IHttpClientFactory HttpClientFactory { get; private set; }
        public HttpClient _httpClient { get; private set; }

        public string QdrantBinaryPath { get; set; }
        public string QdrantDir { get; set; } = Path.Combine(Env.WorkDir, "bin");
        public string QdrantDataPath { get; set; } = Path.Combine(Env.WorkDir, "qdrant_data");
        public string QdrantConfigTemplate { get; set; } = @"log_level: INFO
storage:
  storage_path: {{data_path}}/storage
  snapshots_path: {{data_path}}/snapshots
service:
  static_content_dir: {{data_path}}/static
  api_key: ""{{api_key}}""
  host: 127.0.0.1
  http_port: {{http_port}}
  grpc_port: {{grpc_port}}
";

        private Process _qdrantProcess;
        private bool _isRunning = false;
        public QdrantProcessManager(
            ILogger<QdrantProcessManager> logger,
            IHttpClientFactory HttpClientFactory) {
            this.logger = logger;
            this.HttpClientFactory = HttpClientFactory;
            _httpClient = HttpClientFactory.CreateClient();
            QdrantBinaryPath = Path.Combine(QdrantDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "qdrant.exe" : "qdrant");
            if (!Directory.Exists(QdrantDataPath)) {
                Directory.CreateDirectory(QdrantDataPath);
            }
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
            var tempDir = Path.Combine(Env.WorkDir, "temp");
            if (!Directory.Exists(tempDir)) {
                Directory.CreateDirectory(tempDir);
            }
            var tempFile = Path.Combine(tempDir, $"qdrant-download-{Guid.NewGuid()}.tmp");
            using (var fileStream = new FileStream(tempFile, FileMode.Create)) {
                await response.Content.CopyToAsync(fileStream);
            }

            // 验证ZIP文件有效性
            using (var archive = System.IO.Compression.ZipFile.OpenRead(tempFile)) {
                if (archive.Entries.Count == 0)
                    throw new Exception("Invalid ZIP file - no entries");
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, QdrantDir);
        }

        private string GetDownloadUrlForCurrentPlatform() {
            if (OperatingSystem.IsWindows()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.14.0/qdrant-x86_64-pc-windows-msvc.zip";
            } else if (OperatingSystem.IsLinux()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.14.0/qdrant-x86_64-unknown-linux-gnu.tar.gz";
            } else if (OperatingSystem.IsMacOS()) {
                return "https://github.com/qdrant/qdrant/releases/download/v1.14.0/qdrant-x86_64-apple-darwin.tar.gz";
            }

            throw new PlatformNotSupportedException("Unsupported operating system");
        }

        public async Task StartQdrantAsync() {
            if (_isRunning) return;

            if (!File.Exists(QdrantBinaryPath)) {
                await DownloadQdrantBinaryAsync();
            }
            var template = Template.Parse(QdrantConfigTemplate);
            var result = template.Render(new { data_path = QdrantDataPath, api_key = Env.QdrantApiKey, http_port = Env.QdrantHttpPort, grpc_port = Env.QdrantGrpcPort }); // => "Hello World!" 
            await File.WriteAllTextAsync(Path.Combine(QdrantDataPath, "production.yaml"), result);
            await Task.Delay(1000);
            _qdrantProcess = AppBootstrap.AppBootstrap.Fork(QdrantBinaryPath, [$"--config-path {QdrantDataPath}/production.yaml"]);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await StartQdrantAsync();
        }
    }
}