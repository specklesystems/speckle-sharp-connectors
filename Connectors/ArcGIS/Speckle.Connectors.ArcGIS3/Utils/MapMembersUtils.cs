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

  /// <summary>
  /// Sorts the selected mapmembers into the same order as they appear in the Table of Contents (TOC) bar in the file.
  /// This is a required step before unpacking layers, because depending on the user selection order, some children layers may appear before their container layer if both the container and children layers are selected.
  /// </summary>
  public IEnumerable<MapMember> GetMapMembersInOrder(Map map, IReadOnlyList<MapMember> selectedMapMembers)
  {
    // first get all map layers
    List<MapMember> allMapMembers = GetAllMapMembers(map);

    // recalculate selected layer priority from all map layers
    foreach (MapMember mapMember in allMapMembers)
    {
      if (selectedMapMembers.Contains(mapMember))
      {
        yield return mapMember;
      }
    }
  }
}
