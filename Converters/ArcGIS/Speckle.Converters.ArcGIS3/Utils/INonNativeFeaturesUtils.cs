using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Converters.ArcGIS3.Utils;

public interface INonNativeFeaturesUtils
{
  public void WriteGeometriesToDatasets(
    Dictionary<TraversalContext, List<ObjectConversionTracker>> conversionTracker,
    Action<string, double?>? onOperationProgressed
  );
}
