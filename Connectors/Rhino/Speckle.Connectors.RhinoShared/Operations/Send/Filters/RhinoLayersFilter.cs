using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Rhino.HostApp;

namespace Speckle.Connectors.Rhino.Operations.Send.Filters;

public class RhinoLayersFilter : DiscriminatedObject, ISendFilter
{
  public string Id { get; set; } = "rhinoLayers";
  public string Name { get; set; } = "Layers";
  public string Type { get; set; } = "Select";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; } = [];
  public Dictionary<string, string>? IdMap { get; set; }

  public bool IsMultiSelectable { get; set; } = true;
  public List<SendFilterSelectItem> SelectedItems { get; set; }
  public List<SendFilterSelectItem> Items => GetFilterItems();

  public RhinoLayersFilter() { }

  public List<string> RefreshObjectIds()
  {
    SelectedObjectIds.Clear();
    RhinoDoc doc = RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      return SelectedObjectIds;
    }

    foreach (var item in SelectedItems)
    {
      Layer? layer = RhinoLayerHelper.GetLayer(item.Id);
      if (layer != null)
      {
        var objectIds = doc.Objects.FindByLayer(layer).Select(obj => obj.Id.ToString());
        SelectedObjectIds.AddRange(objectIds);
      }
    }

    return SelectedObjectIds;
  }

  private List<SendFilterSelectItem> GetFilterItems()
  {
    List<SendFilterSelectItem> filterItems = new List<SendFilterSelectItem>();
    RhinoDoc doc = RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      return filterItems;
    }

    foreach (Layer layer in doc.Layers)
    {
      if (!layer.IsDeleted)
      {
        filterItems.Add(new SendFilterSelectItem(layer.Id.ToString(), RhinoLayerHelper.GetFullLayerPath(layer)));
      }
    }

    return filterItems;
  }
}
