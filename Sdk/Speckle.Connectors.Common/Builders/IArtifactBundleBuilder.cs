using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk.Bundles;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Builders;

/// <summary>
/// The Speckle 2026.9.0 send builder: converts host objects into a <see cref="BundleBuilder"/> and hands it back
/// unbuilt. Everything after that — ingestion, version id, file naming, upload, failure reporting — is the SDK's
/// (<see cref="Speckle.Sdk.Bundles.IBundleSender"/>), so a connector's send is exactly its conversion.
/// Successor of <see cref="IArtifactRootObjectBuilder{T}"/>, which owned write + upload itself.
/// </summary>
public interface IArtifactBundleBuilder<in T>
{
  /// <summary>
  /// Converts <paramref name="objects"/> and writes them into a new <see cref="BundleBuilder"/> (the builder streams
  /// to parquet as it goes). The caller owns the returned builder: <see cref="IBundleSender"/> finishes and uploads it,
  /// a headless leg calls <see cref="BundleBuilder.Build"/> itself.
  /// </summary>
  Task<ArtifactBundleBuild> Build(
    IReadOnlyList<T> objects,
    string? projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}

/// <summary>An unbuilt bundle plus the per-object conversion results the report card shows.</summary>
public sealed record ArtifactBundleBuild(BundleBuilder Bundle, IReadOnlyList<SendConversionResult> ConversionResults);
