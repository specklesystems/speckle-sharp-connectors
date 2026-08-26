using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Operations;

/// <summary>Property access for bundles read in the columnar profile (<see cref="ArtefactReadOptions.ColumnarProperties"/>),
/// which is how <see cref="ArtifactReceiver"/> reads them: objects' properties are row-range views over the
/// bundle's <see cref="PropertyTable"/>, keyed by the stored dotted path.</summary>
public static class ArtefactBundleExtensions
{
  /// <summary>All property rows of one object (root scalars such as <c>name</c>/<c>units</c> and the
  /// <c>properties.</c>-prefixed tree alike). Empty view for an object with no rows.</summary>
  public static PropertyView ObjectProperties(this ArtefactBundle bundle, int objK) =>
    (
      bundle.PropertyTable
      ?? throw new InvalidOperationException(
        "Bundle was read in the nested profile; connectors read with ArtefactReadOptions.ColumnarProperties."
      )
    )[objK];
}
