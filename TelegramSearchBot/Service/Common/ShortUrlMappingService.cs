using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Common {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class ShortUrlMappingService : IShortUrlMappingService {
        public string ServiceName => "ShortUrlMappingService";
        private readonly DataDbContext _dbContext;

        public ShortUrlMappingService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public async Task<Dictionary<string, string>> GetUrlMappingsAsync(IEnumerable<string> originalUrls, CancellationToken cancellationToken) {
            var mappings = await _dbContext.ShortUrlMappings
                .Where(m => originalUrls.Contains(m.OriginalUrl) && !string.IsNullOrWhiteSpace(m.ExpandedUrl))
                .GroupBy(m => m.OriginalUrl)
                .Select(g => g.First())
                .ToDictionaryAsync(
                    m => m.OriginalUrl,
                    m => m.ExpandedUrl,
                    cancellationToken);

            return mappings;
        }

        public async Task<int> SaveUrlMappingsAsync(IEnumerable<ShortUrlMapping> mappings, CancellationToken cancellationToken) {
            var distinctMappings = mappings
                .GroupBy(m => m.OriginalUrl)
                .Select(g => g.First())
                .ToList();

            var originalUrls = distinctMappings.Select(m => m.OriginalUrl).ToList();

            var existingMappings = await _dbContext.ShortUrlMappings
                .Where(m => originalUrls.Contains(m.OriginalUrl) && !string.IsNullOrWhiteSpace(m.ExpandedUrl))
                .Select(m => m.OriginalUrl)
                .Distinct()
                .ToListAsync(cancellationToken);

            var newMappings = distinctMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.ExpandedUrl) &&
                           !existingMappings.Contains(m.OriginalUrl))
                .ToList();

            if (newMappings.Any()) {
                _dbContext.ShortUrlMappings.AddRange(newMappings);
                return await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return 0;
        }
    }
}
