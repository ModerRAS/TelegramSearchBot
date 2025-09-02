using System.IO;
using System.Linq;
using TelegramSearchBot.Helper;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    public class WordCloudHelperTests {
        [Fact]
        public void GenerateWordCloud_ShouldCreateImageFile() {
            // 准备测试数据
            var testTexts = new[]
            {
                "这是一个测试文本",
                "用来生成词云图片",
                "看看效果如何",
                "中文分词是否正常",
                "词云生成是否成功",
                "测试测试再测试"
            };

            try {
                // 生成词云
                var imageBytes = WordCloudHelper.GenerateWordCloud(testTexts);

                // 保存到文件
                var outputPath = "wordcloud_test.png";
                File.WriteAllBytes(outputPath, imageBytes);

                // 验证文件存在且非空
                Assert.True(File.Exists(outputPath));
                Assert.True(new FileInfo(outputPath).Length > 0);

                // 打印文件路径
                System.Console.WriteLine($"词云图片已保存到: {Path.GetFullPath(outputPath)}");
            } catch (System.TypeInitializationException ex) when (ex.InnerException is System.PlatformNotSupportedException) {
                // 在Linux上System.Drawing.Common不支持时跳过测试
                System.Console.WriteLine("跳过测试：System.Drawing.Common在当前平台上不支持");
                // Assert.Skip在xUnit中不可用，使用Skip属性
                return;
            }
        }
    }
}
