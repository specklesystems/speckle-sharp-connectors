using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.ArcGIS.Bindings;

public class ArcGISSelectionBinding : ISelectionBinding
{
  private readonly MapMembersUtils _mapMemberUtils;
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public ArcGISSelectionBinding(IBrowserBridge parent, MapMembersUtils mapMemberUtils)
  {
    _mapMemberUtils = mapMemberUtils;
    Parent = parent;
    var topLevelHandler = parent.TopLevelExceptionHandler;

    // example: https://github.com/Esri/arcgis-pro-sdk-community-samples/blob/master/Map-Authoring/QueryBuilderControl/DefinitionQueryDockPaneViewModel.cs
    // MapViewEventArgs args = new(MapView.Active);
    TOCSelectionChangedEvent.Subscribe(_ => topLevelHandler.CatchUnhandled(OnSelectionChanged), true);
  }

  private void OnSelectionChanged()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SelectionBindingEvents.SET_SELECTION, selInfo);
  }

  private void GetLayersFromGroup(GroupLayer group, List<MapMember> nestedLayers)
  {
    nestedLayers.Add(group);
    foreach (MapMember member in group.Layers)
    {
      if (member is GroupLayer subGroup)
      {
        GetLayersFromGroup(subGroup, nestedLayers);
      }
      else
      {
        nestedLayers.Add(member);
      }
    }
  }

  public SelectionInfo GetSelection()
  {
    MapView mapView = MapView.Active;
    List<MapMember> selectedMembers = new();
    selectedMembers.AddRange(mapView.GetSelectedLayers());
    selectedMembers.AddRange(mapView.GetSelectedStandaloneTables());

    List<MapMember> allNestedMembers = new();
    foreach (MapMember member in selectedMembers)
    {
      var layerMapMembers = _mapMemberUtils.UnpackMapLayers(selectedMembers);
      allNestedMembers.AddRange(layerMapMembers);
    }

    List<string> objectTypes = allNestedMembers
      .Select(o => o.GetType().ToString().Split(".").Last())
      .Distinct()
      .ToList();
    return new SelectionInfo(
      allNestedMembers.Select(x => x.URI).ToList(),
      $"{allNestedMembers.Count} layers ({string.Join(", ", objectTypes)})"
    );
  }
}
