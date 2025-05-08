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
            using var reader = new StreamReader(stream);
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
        public void ParseHtml_ShouldHandleEmptyHtml()
        {
            // Arrange
            var service = new DuckDuckGoToolService();
            
            // Act
            var result = service.ParseHtml("", "test query");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Results.Count);
        }
    }
}
