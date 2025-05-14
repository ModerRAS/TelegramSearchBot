using TelegramSearchBot.Model; // For DataDbContext
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM; // For McpTool attributes
using TelegramSearchBot.Attributes; // For McpTool attributes
using System.Linq;
using System.Threading.Tasks; // For async operations
using Microsoft.EntityFrameworkCore; // For EF Core operations
using TelegramSearchBot.Interface; // Added for IService
using System.Collections.Generic; // For List
using System; // For DateTime

namespace TelegramSearchBot.Service.Tools
{
    public class ShortUrlMappingResult
    {
        public string OriginalUrl { get; set; }
        public string ExpandedUrl { get; set; }
        public DateTime CreationDate { get; set; }
    }

    public class ShortUrlToolService : IService
    {
        public string ServiceName => "ShortUrlToolService";

        private readonly DataDbContext _dbContext;

        public ShortUrlToolService(DataDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [McpTool("Retrieves the expanded URL and creation date for a given short URL.")]
        public async Task<ShortUrlMappingResult> GetShortUrlMapping(
            [McpParameter("The short URL (OriginalUrl) to look up.")] string shortUrl)
        {
            if (string.IsNullOrWhiteSpace(shortUrl))
            {
                return null; // Or throw an ArgumentNullException
            }

            var mapping = await _dbContext.ShortUrlMappings
                                          .AsNoTracking()
                                          .FirstOrDefaultAsync(m => m.OriginalUrl == shortUrl);

            if (mapping == null)
            {
                return null; // Not found
            }

            return new ShortUrlMappingResult
            {
                OriginalUrl = mapping.OriginalUrl,
                ExpandedUrl = mapping.ExpandedUrl,
                CreationDate = mapping.CreationDate
            };
        }

        [McpTool("Retrieves all short URL mappings, optionally filtered by a partial original or expanded URL. Supports pagination.")]
        public async Task<List<ShortUrlMappingResult>> GetAllShortUrlMappings(
            [McpParameter("Optional: Text to search within the original URL.", IsRequired = false)] string originalUrlQuery = null,
            [McpParameter("Optional: Text to search within the expanded URL.", IsRequired = false)] string expandedUrlQuery = null,
            [McpParameter("The page number for pagination (e.g., 1, 2, 3...). Defaults to 1.", IsRequired = false)] int page = 1,
            [McpParameter("The number of results per page (e.g., 10, 25). Defaults to 10, max 50.", IsRequired = false)] int pageSize = 10)
        {
            if (pageSize > 50) pageSize = 50;
            if (pageSize <= 0) pageSize = 10;
            if (page <= 0) page = 1;

            int skip = (page - 1) * pageSize;
            int take = pageSize;

            var query = _dbContext.ShortUrlMappings.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(originalUrlQuery))
            {
                query = query.Where(m => m.OriginalUrl.Contains(originalUrlQuery));
            }

            if (!string.IsNullOrWhiteSpace(expandedUrlQuery))
            {
                query = query.Where(m => m.ExpandedUrl.Contains(expandedUrlQuery));
            }

            var mappings = await query.OrderByDescending(m => m.CreationDate)
                                      .Skip(skip)
                                      .Take(take)
                                      .Select(m => new ShortUrlMappingResult
                                      {
                                          OriginalUrl = m.OriginalUrl,
                                          ExpandedUrl = m.ExpandedUrl,
                                          CreationDate = m.CreationDate
                                      })
                                      .ToListAsync();
            return mappings;
        }

        [McpTool("Retrieves the expanded URLs and creation dates for a list of short URLs.")]
        public async Task<List<ShortUrlMappingResult>> GetShortUrlMappingsBatch(
            [McpParameter("A list of short URLs (OriginalUrls) to look up.")] List<string> shortUrls)
        {
            if (shortUrls == null || !shortUrls.Any())
            {
                return new List<ShortUrlMappingResult>();
            }

            // Remove duplicates and null/empty strings to avoid unnecessary queries
            var distinctShortUrls = shortUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToList();

            if (!distinctShortUrls.Any())
            {
                return new List<ShortUrlMappingResult>();
            }

            var mappings = await _dbContext.ShortUrlMappings
                                          .AsNoTracking()
                                          .Where(m => distinctShortUrls.Contains(m.OriginalUrl))
                                          .Select(m => new ShortUrlMappingResult
                                          {
                                              OriginalUrl = m.OriginalUrl,
                                              ExpandedUrl = m.ExpandedUrl,
                                              CreationDate = m.CreationDate
                                          })
                                          .ToListAsync();

            // It's possible that some shortUrls were not found. 
            // The result will only contain mappings for the URLs that were found.
            return mappings;
        }
    }
}
