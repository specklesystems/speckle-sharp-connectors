using ArcGIS.Desktop.Internal.Mapping;
using ArcGIS.Desktop.Mapping;
using ArcLayer = ArcGIS.Desktop.Mapping.Layer;

namespace Speckle.Connectors.ArcGIS.Utils;

public class MapMembersUtils
{
  public List<MapMember> GetMapLayers(Map map)
  {
    // first get all map layers
    Dictionary<MapMember, int> layersIndices = new();
    int count = 0;
    var layers = map.Layers;
    count = UnpackMapLayers(layersIndices, layers, count);

    return layersIndices.Select(x => x.Key).ToList();
  }

  public int UnpackMapLayers(Dictionary<MapMember, int> layersIndices, IEnumerable<ArcLayer> layersToUnpack, int count)
  {
    foreach (var layer in layersToUnpack)
    {
      switch (layer)
      {
        case GroupLayer subGroup:
          layersIndices[layer] = count;
          count++;
          count = UnpackMapLayers(layersIndices, subGroup.Layers, count);
          break;
        case ILayerContainerInternal subLayerContainerInternal:
          layersIndices[layer] = count;
          count++;
          count = UnpackMapLayers(layersIndices, subLayerContainerInternal.InternalLayers, count);
          break;
        default:
          layersIndices[layer] = count;
          count++;
          break;
      }
    }
    return count;
  }
}
