using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;

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
      if (Guid.TryParse(item.Id, out Guid layerId))
      {
        Layer layer = doc.Layers.FindId(layerId);
        if (layer != null)
        {
          var objectIds = doc.Objects.FindByLayer(layer).Select(obj => obj.Id.ToString());
          SelectedObjectIds.AddRange(objectIds);
        }
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
        filterItems.Add(new SendFilterSelectItem(layer.Id.ToString(), GetFullLayerPath(layer)));
      }
    }

    return filterItems;
  }

  private string GetFullLayerPath(Layer layer)
  {
    string fullPath = layer.Name;
    Guid parentIndex = layer.ParentLayerId;
    while (parentIndex != Guid.Empty)
    {
      Layer parentLayer = RhinoDoc.ActiveDoc.Layers.FindId(parentIndex);
      if (parentLayer == null)
      {
        break;
      }

      fullPath = parentLayer.Name + "/" + fullPath;
      parentIndex = parentLayer.ParentLayerId;
    }
    return fullPath;
  }
}
