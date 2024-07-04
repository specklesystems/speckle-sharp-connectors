using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using ArcGIS.Desktop.Mapping.Events;
using ArcGIS.Desktop.Mapping;

namespace Speckle.Connectors.ArcGIS.Bindings;

public class ArcGISSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBridge Parent { get; }

  public ArcGISSelectionBinding(IBridge parent, ITopLevelExceptionHandler topLevelHandler)
  {
    Parent = parent;

    // example: https://github.com/Esri/arcgis-pro-sdk-community-samples/blob/master/Map-Authoring/QueryBuilderControl/DefinitionQueryDockPaneViewModel.cs
    // MapViewEventArgs args = new(MapView.Active);
    TOCSelectionChangedEvent.Subscribe(_ => topLevelHandler.CatchUnhandled(OnSelectionChanged), true);
  }

  private void OnSelectionChanged()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SelectionBindingEvents.SET_SELECTION, selInfo);
  }

  public SelectionInfo GetSelection()
  {
    MapView mapView = MapView.Active;
    List<MapMember> selectedMembers = new();
    selectedMembers.AddRange(mapView.GetSelectedLayers());
    selectedMembers.AddRange(mapView.GetSelectedStandaloneTables());

    List<string> objectTypes = selectedMembers
      .Select(o => o.GetType().ToString().Split(".").Last())
      .Distinct()
      .ToList();
    return new SelectionInfo(
      selectedMembers.Select(x => x.URI).ToList(),
      $"{selectedMembers.Count} layers ({string.Join(", ", objectTypes)})"
    );
  }
}
