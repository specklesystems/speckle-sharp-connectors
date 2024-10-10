using ArcGIS.Core.Events;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.ArcGIS.Bindings;

public sealed class ArcGISSelectionBinding(IBrowserBridge parent, MapMembersUtils mapMembersUtils)
  : ISelectionBinding,
    IPostInitBinding,
    IDisposable
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; } = parent;

  private SubscriptionToken _subscriptionToken;

  public void PostInitialization()
  {
    // example: https://github.com/Esri/arcgis-pro-sdk-community-samples/blob/master/Map-Authoring/QueryBuilderControl/DefinitionQueryDockPaneViewModel.cs
    _subscriptionToken = TOCSelectionChangedEvent.Subscribe(
      _ => Parent.TopLevelExceptionHandler.CatchUnhandled(OnSelectionChanged),
      true
    );
  }

  public void Dispose()
  {
    TOCSelectionChangedEvent.Unsubscribe(_subscriptionToken);
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
      var layerMapMembers = mapMembersUtils.UnpackMapLayers(selectedMembers);
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
