using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.Test.Service.Tools
{
    [TestClass]
    public class DuckDuckGoToolServiceTest
    {
        [TestMethod]
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
            Assert.IsNotNull(result);
            Assert.AreEqual("test query", result.Query);
            Assert.IsTrue(result.Results.Count > 0);
            foreach (var item in result.Results)
            {
                Assert.IsFalse(string.IsNullOrEmpty(item.Title));
                Assert.IsFalse(string.IsNullOrEmpty(item.Url));
            }
        }

        [TestMethod]
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
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Results.Count);
        }

        [TestMethod]
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
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Results.Count, "Expected exactly 2 search result");
            var item = result.Results[0];
            Assert.AreEqual("Test & Result <3>", item.Title, "Title should match with HTML entities decoded");
            Assert.AreEqual("example.com?q=1&w=2", item.Url, "URL should match with special characters");
            Assert.AreEqual("Description with \"quotes\" & special chars", item.Description, "Description should match with special characters");
        }

        [TestMethod]
        public void ParseHtml_ShouldHandleMissingFields()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            var html = "<html><body><div class='result'></div></body></html>";
            
            // Act
            var result = service.ParseHtml(html, "test query");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Results.Count);
        }
    }
}
