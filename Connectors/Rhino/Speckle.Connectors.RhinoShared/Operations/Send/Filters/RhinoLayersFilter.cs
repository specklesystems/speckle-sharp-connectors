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
    // TODO get this from UI
    bool includeSublayers = true;
    RhinoDoc doc = RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      return new List<string>();
    }

    var uniqueIds = includeSublayers ? GetObjectIdsWithSublayers(doc) : GetObjectIdsWithoutSublayers(doc);

    SelectedObjectIds = uniqueIds.ToList();
    return SelectedObjectIds;
  }

  private HashSet<string> GetObjectIdsWithoutSublayers(RhinoDoc doc)
  {
    var result = new HashSet<string>();

    foreach (var item in SelectedItems)
    {
      if (Guid.TryParse(item.Id, out Guid layerId))
      {
        Layer layer = doc.Layers.FindId(layerId);
        if (layer != null)
        {
          foreach (var obj in doc.Objects.FindByLayer(layer))
          {
            result.Add(obj.Id.ToString());
          }
        }
      }
    }

    return result;
  }

  private HashSet<string> GetObjectIdsWithSublayers(RhinoDoc doc)
  {
    var result = new HashSet<string>();

    foreach (var item in SelectedItems)
    {
      if (Guid.TryParse(item.Id, out Guid layerId))
      {
        Layer parentLayer = doc.Layers.FindId(layerId);
        if (parentLayer != null)
        {
          string parentPath = parentLayer.FullPath;

          var layersToSearch = doc.Layers.Where(l =>
            l.FullPath == parentPath || l.FullPath.StartsWith(parentPath + "::", StringComparison.OrdinalIgnoreCase)
          );

          foreach (var layer in layersToSearch)
          {
            foreach (var obj in doc.Objects.FindByLayer(layer))
            {
              result.Add(obj.Id.ToString());
            }
          }
        }
      }
    }

    return result;
  }

  private List<SendFilterSelectItem> GetFilterItems()
  {
    var filterItems = new HashSet<SendFilterSelectItem>();
    RhinoDoc doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return filterItems.ToList();
    }

    foreach (Layer layer in doc.Layers)
    {
      if (!layer.IsDeleted)
      {
        var item = new SendFilterSelectItem(layer.Id.ToString(), GetFullLayerPath(layer));
        filterItems.Add(item);
      }
    }

    return filterItems.ToList();
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
