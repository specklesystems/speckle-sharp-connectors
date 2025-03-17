using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Autocad.HostApp;

public class AutocadLayerUnpacker
{
  private readonly Dictionary<string, Layer> _layerCollectionCache = new();

  public Layer GetOrCreateSpeckleLayer(Entity entity, Transaction tr, out LayerTableRecord? layer)
  {
    string layerName = entity.Layer;
    layer = null;
    if (_layerCollectionCache.TryGetValue(layerName, out Layer? speckleLayer))
    {
      return speckleLayer;
    }
    if (tr.GetObject(entity.LayerId, OpenMode.ForRead) is LayerTableRecord autocadLayer)
    {
      speckleLayer = new Layer(layerName) { applicationId = autocadLayer.GetSpeckleApplicationId() }; // Do not use handle directly, see note in the 'GetSpeckleApplicationId' method
      _layerCollectionCache[layerName] = speckleLayer;
      layer = autocadLayer;
      return speckleLayer;
    }

    throw new SpeckleException("Unexpected condition in GetOrCreateSpeckleLayer");
  }
}
