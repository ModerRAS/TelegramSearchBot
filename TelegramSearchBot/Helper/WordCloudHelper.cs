using System;
using System.IO;
using System.Linq;
using System.Drawing;
using JiebaNet.Segmenter;
using WordCloudSharp;

namespace TelegramSearchBot.Helper
{
    public static class WordCloudHelper
    {
        public static byte[] GenerateWordCloud(string[] words, int width = 800, int height = 600)
        {
            // 中文分词
            var segmenter = new JiebaSegmenter();
            var wordFrequencies = words
                .SelectMany(text => segmenter.Cut(text))
                .Where(word => word.Length > 1) // 过滤单字
                .GroupBy(word => word)
                .ToDictionary(g => g.Key, g => g.Count());

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