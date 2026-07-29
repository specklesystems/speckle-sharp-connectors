using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Builders;

/// <summary>
/// Builds the Speckle 4.0 client-side artefact bundle (SGEO geometries + eav + envelope parquet) and
/// uploads it via the v2 data endpoints. This is the "no serialized Base graph" send path: unlike
/// <see cref="IRootObjectBuilder{T}"/> (which returns a <see cref="Speckle.Sdk.Models.Base"/> graph for
/// the v1 serializer), the artefact path owns its own write + upload, so it returns only the resulting
/// (pre-allocated, now committed) version id, a synthetic root id, and the per-object conversion results.
/// </summary>
public interface IArtifactRootObjectBuilder<in T>
{
  /// <summary>
  /// Converts <paramref name="objects"/>, writes the artefact bundle to local disk, and uploads it under
  /// the server pre-allocated <paramref name="versionId"/> via the ingestion's v2 upload endpoints
  /// (sign → presigned PUT per file → complete, which creates the version).
  /// </summary>
  Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<T> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}

/// <summary>Result of an artefact-bundle send: the committed version id (echoes the pre-allocated id),
/// the synthetic root id the version was completed against, and the per-object conversion results.</summary>
public record ArtifactBuildResult(
  string VersionId,
  string RootId,
  IReadOnlyList<SendConversionResult> ConversionResults
);

/// <summary>Result of the build-only phase (bundle written to a local output directory, nothing uploaded):
/// the bundle's parquet files keyed by file name, the synthetic root id, the count of successfully
/// converted objects, and the per-object conversion results. Consumed by <c>BuildAndUpload</c>'s upload
/// phase — and directly by headless hosts whose upload happens out of process (the converter legs).</summary>
public record ArtifactBundleResult(
  IReadOnlyDictionary<string, string> Bundle,
  string RootId,
  int ObjectCount,
  IReadOnlyList<SendConversionResult> ConversionResults
);
