using ArcGIS.Desktop.Internal.Mapping;
using ArcGIS.Desktop.Mapping;

namespace Speckle.Connectors.ArcGIS.Utils;

public class MapMembersUtils
{
  public List<MapMember> GetAllMapMembers(Map map)
  {
    // first get all map layers
    List<MapMember> mapMembers = new();
    var layers = map.Layers;
    UnpackMapLayers(mapMembers, layers);
    UnpackMapLayers(mapMembers, map.StandaloneTables);
    return mapMembers;
  }

  public void UnpackMapLayers(List<MapMember> mapMembers, IEnumerable<MapMember> mapMembersToUnpack)
  {
    foreach (var layer in mapMembersToUnpack)
    {
      switch (layer)
      {
        case GroupLayer subGroup:
          mapMembers.Add(layer);
          UnpackMapLayers(mapMembers, subGroup.Layers);
          break;
        case ILayerContainerInternal subLayerContainerInternal:
          mapMembers.Add(layer);
          UnpackMapLayers(mapMembers, subLayerContainerInternal.InternalLayers);
          break;
        default:
          mapMembers.Add(layer);
          break;
      }
    }
  }

  // Gets the layer display priority for selected layers
  public List<(MapMember, int)> GetLayerDisplayPriority(Map map, IReadOnlyList<MapMember> selectedMapMembers)
  {
    // first get all map layers
    List<MapMember> allMapMembers = GetAllMapMembers(map);

    // recalculate selected layer priority from all map layers
    List<(MapMember, int)> selectedLayers = new();
    int newCount = 0;
    foreach (MapMember mapMember in allMapMembers)
    {
      if (selectedMapMembers.Contains(mapMember))
      {
        selectedLayers.Add((mapMember, newCount));
        newCount++;
      }
    }

    return selectedLayers;
  }
}
