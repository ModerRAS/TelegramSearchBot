using System.Text.Json;
using Moder.Update.Models;

namespace Moder.Update.Demo;

/// <summary>
/// IUpdateCatalogFetcher implementation that reads from local filesystem.
/// Used for testing without an HTTP server.
/// </summary>
public class LocalUpdateCatalogFetcher : IUpdateCatalogFetcher
{
    private readonly string _basePath;

    public LocalUpdateCatalogFetcher(string basePath)
    {
        _basePath = Path.GetFullPath(basePath);
    }

    public async Task<string> FetchCatalogAsync(CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(_basePath, "catalog.json");
        if (!File.Exists(catalogPath))
            throw new FileNotFoundException($"Catalog not found: {catalogPath}");
        return await File.ReadAllTextAsync(catalogPath, ct);
    }

    public Task<Stream> DownloadPackageAsync(string packagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, packagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Package not found: {fullPath}");
        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }
}
