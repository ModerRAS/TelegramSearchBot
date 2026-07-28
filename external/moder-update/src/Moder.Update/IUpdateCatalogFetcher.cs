using Moder.Update.Models;

namespace Moder.Update;

/// <summary>
/// Interface for consumers to provide HTTP fetching capability.
/// The library does not include an HTTP client — consumers implement this interface.
/// </summary>
public interface IUpdateCatalogFetcher
{
    /// <summary>Fetches the update catalog JSON string from the catalog URL.</summary>
    Task<string> FetchCatalogAsync(CancellationToken ct = default);

    /// <summary>Downloads a package and returns a stream to its content.</summary>
    Task<Stream> DownloadPackageAsync(string packagePath, CancellationToken ct = default);
}
