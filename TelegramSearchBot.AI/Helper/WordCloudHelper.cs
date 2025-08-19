using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using JiebaNet.Segmenter;
using WordCloudSharp;
using Serilog;

namespace TelegramSearchBot.Helper
{
    public static class WordCloudHelper
    {
        private static readonly object _lock = new object();
        private static JiebaSegmenter _segmenter;

        private static JiebaSegmenter GetSegmenter()
        {
            if (_segmenter == null)
            {
                lock (_lock)
                {
                    if (_segmenter == null)
                    {
                        try
                        {
                            // 确保JiebaNet资源文件已下载
                            if (!JiebaResourceDownloader.AreAllResourceFilesPresent())
                            {
                                // 如果资源文件不存在，尝试下载
                                var downloadTask = JiebaResourceDownloader.EnsureResourcesDownloadedAsync();
                                downloadTask.Wait();
                            }

                            // 直接使用我们下载的资源目录创建JiebaSegmenter
                            var resourceDir = JiebaResourceDownloader.GetResourceDirectory();
                            _segmenter = JiebaResourceDownloader.CreateSegmenterWithCustomPath(resourceDir);
                        }
                        catch (Exception ex)
                        {
                            // 如果JiebaNet初始化失败，使用简单的空格分词作为后备方案
                            Log.Warning($"JiebaNet初始化失败，使用简单分词: {ex.Message}");
                            _segmenter = null;
                        }
                    }
                }
            }
            return _segmenter;
        }

        public static byte[] GenerateWordCloud(string[] words, int width = 1280, int height = 1280)
        {
            var segmenter = GetSegmenter();
            
            // 生成词频字典
            var wordFrequencies = new Dictionary<string, int>();
            
            if (segmenter != null)
            {
                // 使用JiebaNet分词
                foreach (var text in words)
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    
                    try
                    {
                        var segments = segmenter.Cut(text);
                        foreach (var word in segments)
                        {
                            if (word.Length > 1) // 过滤单字
                            {
                                // 简化实现：使用TryGetValue避免潜在的空引用问题
                                // 原本实现：使用wordFrequencies.GetValueOrDefault(word, 0) + 1
                                // 简化实现：使用更安全的字典访问方式
                                if (wordFrequencies.TryGetValue(word, out var count))
                                {
                                    wordFrequencies[word] = count + 1;
                                }
                                else
                                {
                                    wordFrequencies[word] = 1;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"分词失败，使用原文: {ex.Message}");
                        // 分词失败时直接使用原文
                        if (text.Length > 1)
                        {
                            // 简化实现：使用TryGetValue避免潜在的空引用问题
                            // 原本实现：使用wordFrequencies.GetValueOrDefault(text, 0) + 1
                            // 简化实现：使用更安全的字典访问方式
                            if (wordFrequencies.TryGetValue(text, out var count))
                            {
                                wordFrequencies[text] = count + 1;
                            }
                            else
                            {
                                wordFrequencies[text] = 1;
                            }
                        }
                    }
                }
            }
            else
            {
                // JiebaNet不可用时，使用简单的空格和标点符号分词
                foreach (var text in words)
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    
                    var simpleWords = text.Split(new char[] { ' ', '\n', '\r', '\t', '。', '，', '？', '！', '、', '；', '：' }, 
                        StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var word in simpleWords)
                    {
                        if (word.Length > 1) // 过滤单字
                        {
                            // 简化实现：使用TryGetValue避免潜在的空引用问题
                            // 原本实现：使用wordFrequencies.GetValueOrDefault(word, 0) + 1
                            // 简化实现：使用更安全的字典访问方式
                            if (wordFrequencies.TryGetValue(word, out var count))
                            {
                                wordFrequencies[word] = count + 1;
                            }
                            else
                            {
                                wordFrequencies[word] = 1;
                            }
                        }
                    }
                }
            }

            // 如果没有有效词汇，创建默认内容
            if (wordFrequencies.Count == 0)
            {
                wordFrequencies["暂无内容"] = 1;
            }

            // 生成词云
            var wordcloud = new WordCloud(width, height, fontname: "Microsoft YaHei");
            
            using var image = wordcloud.Draw(
                wordFrequencies.Keys.ToArray(),
                wordFrequencies.Values.ToArray()
            );
            
            using var ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
    }
}