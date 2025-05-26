using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Test.Service.Common
{
    [TestClass]
    public class ShortUrlMappingServiceTests : IDisposable
    {
        private readonly DataDbContext _dbContext;
        private readonly ShortUrlMappingService _service;

        public ShortUrlMappingServiceTests()
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            
            _dbContext = new DataDbContext(options);
            _service = new ShortUrlMappingService(_dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [TestMethod]
        public async Task SaveUrlMappingsAsync_ShouldSaveNewMappings()
        {
            // Arrange
            var mappings = new List<ShortUrlMapping>
            {
                new() { OriginalUrl = "short1", ExpandedUrl = "long1" },
                new() { OriginalUrl = "short2", ExpandedUrl = "long2" }
            };

            // Act
            var result = await _service.SaveUrlMappingsAsync(mappings, CancellationToken.None);

            // Assert
            Assert.AreEqual(2, result);
            Assert.AreEqual(2, await _dbContext.ShortUrlMappings.CountAsync());
        }

        [TestMethod]
        public async Task SaveUrlMappingsAsync_ShouldNotSaveDuplicates()
        {
            // Arrange
            var existing = new ShortUrlMapping { OriginalUrl = "short1", ExpandedUrl = "long1" };
            await _dbContext.ShortUrlMappings.AddAsync(existing);
            await _dbContext.SaveChangesAsync();

            var mappings = new List<ShortUrlMapping>
            {
                new() { OriginalUrl = "short1", ExpandedUrl = "long1" },
                new() { OriginalUrl = "short2", ExpandedUrl = "long2" }
            };

            // Act
            var result = await _service.SaveUrlMappingsAsync(mappings, CancellationToken.None);

            // Assert
            Assert.AreEqual(1, result); // Only short2 should be saved
            Assert.AreEqual(2, await _dbContext.ShortUrlMappings.CountAsync());
        }

        [TestMethod]
        public async Task GetUrlMappingsAsync_ShouldReturnCorrectMappings()
        {
            // Arrange
            await _dbContext.ShortUrlMappings.AddRangeAsync(new[]
            {
                new ShortUrlMapping { OriginalUrl = "short1", ExpandedUrl = "long1" },
                new ShortUrlMapping { OriginalUrl = "short2", ExpandedUrl = "long2" },
                new ShortUrlMapping { OriginalUrl = "short1", ExpandedUrl = "long1_duplicate" } // Should be ignored
            });
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.GetUrlMappingsAsync(new[] { "short1", "short2" }, CancellationToken.None);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("long1", result["short1"]); // First one should be returned
            Assert.AreEqual("long2", result["short2"]);
        }

        [TestMethod]
        public async Task GetUrlMappingsAsync_ShouldIgnoreEmptyExpandedUrls()
        {
            // Arrange
            await _dbContext.ShortUrlMappings.AddRangeAsync(new[]
            {
                new ShortUrlMapping { OriginalUrl = "short1", ExpandedUrl = "" }, // Should be ignored
                new ShortUrlMapping { OriginalUrl = "short2", ExpandedUrl = "long2" }
            });
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.GetUrlMappingsAsync(new[] { "short1", "short2" }, CancellationToken.None);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("long2", result["short2"]);
        }
    }
}