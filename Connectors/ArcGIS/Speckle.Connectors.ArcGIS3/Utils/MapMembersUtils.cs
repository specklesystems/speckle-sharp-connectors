using ArcGIS.Desktop.Mapping;

namespace Speckle.Connectors.ArcGIS.Utils;

public class MapMembersUtils
{
  /// <summary>
  /// Returns all Layers and Standalone Tables present on the Map
  /// </summary>
  /// <param name="map"></param>
  /// <returns></returns>
  public List<MapMember> GetAllMapMembers(Map map)
  {
    // first get all map layers
    List<MapMember> mapMembers = new();
    var layerMapMembers = UnpackMapLayers(map.Layers);
    mapMembers.AddRange(layerMapMembers);

    // add tables
    var standaloneTableMapMembers = UnpackMapLayers(map.StandaloneTables);
    mapMembers.AddRange(standaloneTableMapMembers);
    return mapMembers;
  }

  public List<MapMember> UnpackMapLayers(IEnumerable<MapMember> mapMembersToUnpack)
  {
    List<MapMember> mapMembers = new();
    foreach (var layer in mapMembersToUnpack)
    {
      mapMembers.Add(layer);
      switch (layer)
      {
        case ILayerContainer subGroup:
          var subLayerMapMembers = UnpackMapLayers(subGroup.Layers);
          mapMembers.AddRange(subLayerMapMembers);
          break;
      }
    }

    return mapMembers;
  }

  // Gets the layer display priority for selected layers
  public List<MapMember> GetMapMembersInOrder(Map map, IReadOnlyList<MapMember> selectedMapMembers)
  {
    // first get all map layers
    List<MapMember> allMapMembers = GetAllMapMembers(map);

    // recalculate selected layer priority from all map layers
    List<MapMember> selectedLayers = new();
    foreach (MapMember mapMember in allMapMembers)
    {
      if (selectedMapMembers.Contains(mapMember))
      {
        selectedLayers.Add((mapMember));
      }
    }

    return selectedLayers;
  }
}
