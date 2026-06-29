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
/// <remarks>
/// Registered ONLY on targets where the SDK producer is available (.NET 8+: the
/// <c>ObjectsArtifactPipeline</c> / <c>ArtifactPipeline</c> types are <c>#if NET8_0_OR_GREATER</c>). On
/// older targets the implementation is absent, the optional dependency in <c>SendOperation</c> stays
/// <see langword="null"/>, and the send falls back to the v1 ingestion / version-create paths.
/// </remarks>
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
