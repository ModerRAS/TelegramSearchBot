using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Helper
{
    public static class JiebaResourceDownloader
    {
        private static readonly string BaseGitHubUrl = "https://raw.githubusercontent.com/anderscui/jieba.NET/master/src/Segmenter/Resources";
        private static readonly string JiebaResourceDir = Path.Combine(Env.WorkDir, "jieba_resources");
        
        // JiebaNet所需的资源文件列表
        private static readonly List<string> RequiredResourceFiles = new List<string>
        {
            "char_state_tab.json",
            "dict.txt",
            "idf.txt",
            "pos_prob_emit.json",
            "pos_prob_start.json", 
            "pos_prob_trans.json",
            "prob_emit.json",
            "prob_trans.json",
            "stopwords.txt"
        };

        /// <summary>
        /// 确保JiebaNet资源文件已下载并配置路径
        /// </summary>
        /// <returns>JiebaNet配置目录路径</returns>
        public static async Task<string> EnsureResourcesDownloadedAsync()
        {
            try
            {
                // 创建资源目录
                if (!Directory.Exists(JiebaResourceDir))
                {
                    Directory.CreateDirectory(JiebaResourceDir);
                    Log.Information("创建JiebaNet资源目录: {ResourceDir}", JiebaResourceDir);
                }

                // 检查并下载缺失的资源文件
                var missingFiles = GetMissingResourceFiles();
                if (missingFiles.Count > 0)
                {
                    Log.Information("发现 {MissingCount} 个缺失的JiebaNet资源文件，开始下载...", missingFiles.Count);
                    await DownloadResourceFilesAsync(missingFiles);
                    Log.Information("JiebaNet资源文件下载完成");
                }
                else
                {
                    Log.Information("JiebaNet资源文件已存在，跳过下载");
                }

                Log.Information("JiebaNet资源目录已准备: {ResourceDir}", JiebaResourceDir);

                return JiebaResourceDir;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "下载JiebaNet资源文件时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 获取缺失的资源文件列表
        /// </summary>
        private static List<string> GetMissingResourceFiles()
        {
            var missingFiles = new List<string>();
            
            foreach (var fileName in RequiredResourceFiles)
            {
                var filePath = Path.Combine(JiebaResourceDir, fileName);
                if (!File.Exists(filePath))
                {
                    missingFiles.Add(fileName);
                }
            }
            
            return missingFiles;
        }

        /// <summary>
        /// 下载指定的资源文件
        /// </summary>
        private static async Task DownloadResourceFilesAsync(List<string> filesToDownload)
        {
            using var httpClient = HttpClientHelper.CreateProxyHttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // 设置超时时间

            // 添加必要的请求头
            httpClient.DefaultRequestHeaders.Add("User-Agent", "TelegramSearchBot/1.0");

            var downloadTasks = new List<Task>();
            
            foreach (var fileName in filesToDownload)
            {
                downloadTasks.Add(DownloadSingleFileAsync(httpClient, fileName));
            }
            
            await Task.WhenAll(downloadTasks);
        }

        /// <summary>
        /// 下载单个文件
        /// </summary>
        private static async Task DownloadSingleFileAsync(HttpClient httpClient, string fileName)
        {
            try
            {
                var downloadUrl = $"{BaseGitHubUrl}/{fileName}";
                var filePath = Path.Combine(JiebaResourceDir, fileName);

                Log.Information("下载JiebaNet资源文件: {FileName}", fileName);

                using var response = await httpClient.GetAsync(downloadUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(filePath, content);
                    Log.Information("成功下载: {FileName} ({Size} bytes)", fileName, content.Length);
                }
                else
                {
                    Log.Error("下载失败: {FileName}, HTTP状态码: {StatusCode}", fileName, response.StatusCode);
                    throw new HttpRequestException($"下载 {fileName} 失败，HTTP状态码: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "下载文件 {FileName} 时发生错误", fileName);
                throw;
            }
        }

        /// <summary>
        /// 检查所有必需的资源文件是否存在
        /// </summary>
        public static bool AreAllResourceFilesPresent()
        {
            if (!Directory.Exists(JiebaResourceDir))
            {
                return false;
            }

            foreach (var fileName in RequiredResourceFiles)
            {
                var filePath = Path.Combine(JiebaResourceDir, fileName);
                if (!File.Exists(filePath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取JiebaNet资源目录路径
        /// </summary>
        public static string GetResourceDirectory()
        {
            return JiebaResourceDir;
        }

        /// <summary>
        /// 清理并重新下载所有资源文件
        /// </summary>
        public static async Task ForceRedownloadResourcesAsync()
        {
            try
            {
                // 删除现有资源目录
                if (Directory.Exists(JiebaResourceDir))
                {
                    Directory.Delete(JiebaResourceDir, true);
                    Log.Information("已删除现有JiebaNet资源目录");
                }

                // 重新下载
                await EnsureResourcesDownloadedAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "强制重新下载JiebaNet资源文件时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 使用自定义资源路径创建JiebaSegmenter
        /// </summary>
        public static JiebaNet.Segmenter.JiebaSegmenter CreateSegmenterWithCustomPath(string resourceDir)
        {
            try
            {
                // 设置环境变量来指定JiebaNet的配置文件目录
                var originalValue = Environment.GetEnvironmentVariable("JIEBA_DEFAULT_DICT_DIR");
                Environment.SetEnvironmentVariable("JIEBA_DEFAULT_DICT_DIR", resourceDir);
                
                var segmenter = new JiebaNet.Segmenter.JiebaSegmenter();
                
                // 恢复原始环境变量值
                if (originalValue != null)
                {
                    Environment.SetEnvironmentVariable("JIEBA_DEFAULT_DICT_DIR", originalValue);
                }
                else
                {
                    Environment.SetEnvironmentVariable("JIEBA_DEFAULT_DICT_DIR", null);
                }
                
                return segmenter;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "使用自定义路径创建JiebaSegmenter失败: {ResourceDir}", resourceDir);
                throw;
            }
        }
    }
} 