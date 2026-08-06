using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Utils;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Operations;

/// <summary>
/// Receive side of the Speckle 4.0 artefact path: probes for + downloads a version's parquet bundle (geometries +
/// eav + envelope) and parses it into a neutral <see cref="ArtefactBundle"/>. The connector then either bakes the
/// bundle directly (an <c>IArtifactHostObjectBuilder</c>, e.g. Rhino) or reconstructs a <see cref="Base"/> graph for
/// the v1 host-build path (<see cref="Reconstruct"/>, e.g. Revit). Registered per-connector with an
/// <see cref="ArtifactReceiveOptions"/> (Rhino prefers 3dm solids; Revit reconstructs meshes only).
/// </summary>
[GenerateAutoInterface]
public class ArtifactReceiver(
  IArtifactDownloader downloader,
  ArtifactReceiveOptions options,
  ILogger<ArtifactReceiver> logger
) : IArtifactReceiver
{
  /// <summary>
  /// Downloads + parses the version's artefact bundle, or returns <see langword="null"/> when the version has no
  /// bundle (not a 4.0 artefact version, or the server lacks the v2 data endpoints) so the caller can fall back to the
  /// v1 receive path. Detection is by probing the artefacts endpoint — no reliance on id conventions.
  /// </summary>
  public async Task<ArtefactBundle?> TryGetBundleAsync(
    Account account,
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    onOperationProgressed.Report(new("Checking for artefacts...", null));
    var bundleDir = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      "Speckle",
      "receive",
      receiveInfo.SelectedVersionId
    );

    IReadOnlyList<string> files = await downloader
      .DownloadBundleAsync(
        account,
        receiveInfo.ProjectId,
        receiveInfo.ModelId,
        receiveInfo.SelectedVersionId,
        bundleDir,
        cancellationToken
      )
      .ConfigureAwait(false);

    if (files.Count == 0)
    {
      logger.LogInformation(
        "No 4.0 artefact bundle for version {VersionId}; falling back to the v1 receive path.",
        receiveInfo.SelectedVersionId
      );
      return null;
    }

    logger.LogInformation(
      "Downloaded {FileCount} artefact files for version {VersionId}; parsing (preferSolids={PreferSolids}).",
      files.Count,
      receiveInfo.SelectedVersionId,
      options.PreferSolids
    );

    onOperationProgressed.Report(new("Reading artefacts...", null));
    return await ArtefactBundleReader.ReadAsync(bundleDir, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Maps a parsed bundle into a <see cref="Base"/>/<c>Collection</c> graph for the v1 host-build path. Honours the
  /// <c>ArtifactReceiveOptions.PreferSolids</c> flag. Obsolete: every connector that receives the new object model
  /// bakes the bundle directly (<c>IArtifactHostObjectBuilder</c>); reconstruct-then-convert loses data and speed at
  /// the seam and must not be the starting point for new connectors.
  /// </summary>
  [Obsolete(
    "Reconstruct-then-convert is not the way forward for new-object-model receives: register a dedicated "
      + "IArtifactHostObjectBuilder (direct bundle bake) instead. This fallback remains only so a connector without "
      + "a direct-bake builder still receives; no new callers."
  )]
  public Base Reconstruct(ArtefactBundle bundle, CancellationToken cancellationToken) =>
    new ObjectsArtifactReader().Build(bundle, options, cancellationToken);
}
