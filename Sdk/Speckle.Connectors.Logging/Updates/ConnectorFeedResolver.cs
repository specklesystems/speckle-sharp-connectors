using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Onova.Services;
#if NET48
using System.Net.Http;
#endif

namespace Speckle.Connectors.Logging.Updates;

public class ConnectorFeedResolver(string slug, ILogger<ConnectorFeedResolver> logger) : IPackageResolver
{
  private readonly struct ConnectorVersions
  {
    public ConnectorVersion[] Versions { get; init; }
  }

  private readonly Dictionary<Version, ConnectorVersion> _versions = new();

  public async Task<IReadOnlyList<Version>> GetPackageVersionsAsync(CancellationToken cancellationToken)
  {
    var feed = $"https://releases.speckle.dev/manager2/feeds/{slug.ToLowerInvariant()}-v3.json";
    logger.LogInformation($"Getting package versions from {feed}");
    using HttpClient client = new();
    using var response = await client.GetAsync(new Uri(feed), cancellationToken).ConfigureAwait(false);

    response.EnsureSuccessStatusCode();

    var feedData = await JsonSerializer.DeserializeAsync<ConnectorVersions>(
#if NETSTANDARD2_0
      await response.Content.ReadAsStreamAsync(),
#else
      await response.Content.ReadAsStreamAsync(cancellationToken),
#endif
      cancellationToken: cancellationToken
    );

    _versions.Clear();
    foreach (var connectorVersion in feedData.Versions)
    {
      var semVersion = NuGetVersion.Parse(connectorVersion.Number);
      if (semVersion.IsPrerelease) //use a tag for channels?
      {
        continue;
      }
      _versions.Add(semVersion.Version, connectorVersion);
    }
    return _versions.Keys.ToList();
  }

  public async Task DownloadPackageAsync(
    Version version,
    string destFilePath,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default
  )
  {
    if (!_versions.TryGetValue(version, out var connectorVersion))
    {
      throw new InvalidOperationException($"Connector version {version} not found in feed.");
    }
    logger.LogInformation($"Downloading package from {connectorVersion.Url}");
    using HttpClient client = new();
    using var response = await client.GetAsync(connectorVersion.Url, cancellationToken);
    response.EnsureSuccessStatusCode();
#if NETSTANDARD2_0
    using var stream = await response.Content.ReadAsStreamAsync();
#else
    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
#endif
    using var fileStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write);
#if NETSTANDARD2_0
    await stream.CopyToAsync(fileStream);
#else
    await stream.CopyToAsync(fileStream, cancellationToken);
#endif
  }
}
