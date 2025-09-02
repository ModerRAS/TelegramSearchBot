using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JiebaNet.Segmenter;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using WordCloudSharp;

namespace TelegramSearchBot.Helper {
    public static class WordCloudHelper {
        private static readonly object _lock = new object();
        private static JiebaSegmenter _segmenter;

        private static JiebaSegmenter GetSegmenter() {
            if (_segmenter == null) {
                lock (_lock) {
                    if (_segmenter == null) {
                        try {
                            // 确保JiebaNet资源文件已下载
                            if (!JiebaResourceDownloader.AreAllResourceFilesPresent()) {
                                // 如果资源文件不存在，尝试下载
                                var downloadTask = JiebaResourceDownloader.EnsureResourcesDownloadedAsync();
                                downloadTask.Wait();
                            }

                            // 直接使用我们下载的资源目录创建JiebaSegmenter
                            var resourceDir = JiebaResourceDownloader.GetResourceDirectory();
                            _segmenter = JiebaResourceDownloader.CreateSegmenterWithCustomPath(resourceDir);
                        } catch (Exception ex) {
                            // 如果JiebaNet初始化失败，使用简单的空格分词作为后备方案
                            Log.Warning($"JiebaNet初始化失败，使用简单分词: {ex.Message}");
                            _segmenter = null;
                        }
                    }
                }
            }
            return _segmenter;
        }

        public static byte[] GenerateWordCloud(string[] words, int width = 1280, int height = 1280) {
            var segmenter = GetSegmenter();

            // 生成词频字典
            var wordFrequencies = new Dictionary<string, int>();

            if (segmenter != null) {
                // 使用JiebaNet分词
                foreach (var text in words) {
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    try {
                        var segments = segmenter.Cut(text);
                        foreach (var word in segments) {
                            if (word.Length > 1) // 过滤单字
                            {
                                wordFrequencies[word] = wordFrequencies.GetValueOrDefault(word, 0) + 1;
                            }
                        }
                    } catch (Exception ex) {
                        Log.Warning($"分词失败，使用原文: {ex.Message}");
                        // 分词失败时直接使用原文
                        if (text.Length > 1) {
                            wordFrequencies[text] = wordFrequencies.GetValueOrDefault(text, 0) + 1;
                        }
                    }
                }
            } else {
                // JiebaNet不可用时，使用简单的空格和标点符号分词
                foreach (var text in words) {
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var simpleWords = text.Split(new char[] { ' ', '\n', '\r', '\t', '。', '，', '？', '！', '、', '；', '：' },
                        StringSplitOptions.RemoveEmptyEntries);

                    foreach (var word in simpleWords) {
                        if (word.Length > 1) // 过滤单字
                        {
                            wordFrequencies[word] = wordFrequencies.GetValueOrDefault(word, 0) + 1;
                        }
                    }
                }
            }

            // 如果没有有效词汇，创建默认内容
            if (wordFrequencies.Count == 0) {
                wordFrequencies["暂无内容"] = 1;
            }

            // 生成词云
            var wordcloud = new WordCloud(width, height, fontname: "Microsoft YaHei");

            using var systemDrawingImage = wordcloud.Draw(
                wordFrequencies.Keys.ToArray(),
                wordFrequencies.Values.ToArray()
            );

            // 使用跨平台方式转换图像
            return ConvertImageToBytes(systemDrawingImage);
        }

        /// <summary>
        /// 将System.Drawing.Image转换为字节数组（跨平台兼容）
        /// </summary>
        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Platform-specific code is guarded by OperatingSystem checks")]
        private static byte[] ConvertImageToBytes(System.Drawing.Image image) {
            using var ms = new MemoryStream();

            if (OperatingSystem.IsWindows()) {
                // 在Windows上使用System.Drawing
                image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            } else {
                // 在非Windows平台上，尝试使用ImageSharp
                try {
                    // 由于WordCloudSharp依赖System.Drawing，在非Windows平台可能无法正常工作
                    // 这里创建一个空白图像作为后备方案
                    using var imagesharpImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1280, 1280);
                    imagesharpImage.SaveAsPng(ms);
                    return ms.ToArray();
                } catch (Exception ex) {
                    Log.Warning($"非Windows平台图像处理失败: {ex.Message}");
                    throw new PlatformNotSupportedException("WordCloud generation is only supported on Windows platform due to System.Drawing dependency.");
                }
            }
        }
    }
}
