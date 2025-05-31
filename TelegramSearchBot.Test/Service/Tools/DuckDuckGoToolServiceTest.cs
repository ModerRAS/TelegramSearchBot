using System.IO;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools
{
    public class DuckDuckGoToolServiceTest
    {
        [Fact]
        public void ParseHtml_ShouldReturnCorrectResults()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            var assembly = typeof(DuckDuckGoToolServiceTest).Assembly;
            using var stream = assembly.GetManifestResourceStream("TelegramSearchBot.Test.TestData.DuckDuckGoSearchResult.html");
#pragma warning disable CS8604 // 引用类型参数可能为 null。
            using var reader = new StreamReader(stream);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            var html = reader.ReadToEnd();
            
            // Act
            var result = service.ParseHtml(html, "test query");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test query", result.Query);
            Assert.True(result.Results.Count > 0);
            foreach (var item in result.Results)
            {
                Assert.False(string.IsNullOrEmpty(item.Title));
                Assert.False(string.IsNullOrEmpty(item.Url));
            }
        }

        [Fact]
        public void ParseHtml_ShouldHandleEmptyResults()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            var assembly = typeof(DuckDuckGoToolServiceTest).Assembly;
            using var stream = assembly.GetManifestResourceStream("TelegramSearchBot.Test.TestData.EmptyResults.html");
#pragma warning disable CS8604 // 引用类型参数可能为 null。
            using var reader = new StreamReader(stream);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            var html = reader.ReadToEnd();
            
            // Act
            var result = service.ParseHtml(html, "test query");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Results);
        }

        [Fact]
        public void ParseHtml_ShouldHandleSpecialCharacters()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            var assembly = typeof(DuckDuckGoToolServiceTest).Assembly;
            using var stream = assembly.GetManifestResourceStream("TelegramSearchBot.Test.TestData.SpecialCharsResults.html");
#pragma warning disable CS8604 // 引用类型参数可能为 null。
            using var reader = new StreamReader(stream);
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            var html = reader.ReadToEnd();
            
            // Act
            var result = service.ParseHtml(html, "test query");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Results.Count);
            var item = result.Results[0];
            Assert.Equal("Test & Result <3>", item.Title);
            Assert.Equal("example.com?q=1&w=2", item.Url);
            Assert.Equal("Description with \"quotes\" & special chars", item.Description);
        }

        [Fact]
        public void ParseHtml_ShouldHandleMissingFields()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            var html = "<html><body><div class='result'></div></body></html>";
            
            // Act
            var result = service.ParseHtml(html, "test query");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Results);
        }
    }
}
