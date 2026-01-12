using Speckle.InterfaceGenerator;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Connector.Navisworks.Services;

[GenerateAutoInterface]
public class ElementSelectionService : IElementSelectionService
{
  private readonly Dictionary<Guid, bool> _visibleCache = new();

  public string GetModelItemPath(NAV.ModelItem modelItem) => ResolveModelItemToIndexPath(modelItem);

  public NAV.ModelItem GetModelItemFromPath(string path) => ResolveIndexPathToModelItem(path);

  public bool IsVisible(NAV.ModelItem modelItem)
  {
    var key = modelItem.InstanceGuid;
    if (_visibleCache.TryGetValue(key, out var isVisible))
    {
      return isVisible;
    }
    //same as ElementSelectionHelper.IsElementVisible
    // Check and cache ancestors, short-circuit on first hidden
    foreach (var item in modelItem.AncestorsAndSelf)
    {
      if (!_visibleCache.TryGetValue(item.InstanceGuid, out var visible))
      {
        visible = !item.IsHidden;
        _visibleCache[item.InstanceGuid] = visible;
      }

      if (!visible) // Ancestor is hidden, item must be hidden
      {
        // Cache the result for this item too
        _visibleCache[key] = false;
        return false;
      }
    }

    // All ancestors visible
    _visibleCache[key] = true;
    return true;
  }

  public IEnumerable<NAV.ModelItem> GetGeometryNodes(NAV.ModelItem modelItem) => ResolveGeometryLeafNodes(modelItem);
}
