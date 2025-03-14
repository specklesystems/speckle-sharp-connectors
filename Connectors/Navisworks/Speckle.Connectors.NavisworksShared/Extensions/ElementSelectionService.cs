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
    foreach (var item in modelItem.AncestorsAndSelf)
    {
      _visibleCache[item.InstanceGuid] = !item.IsHidden;
    }
    return _visibleCache[key];
  }

  public IEnumerable<NAV.ModelItem> GetGeometryNodes(NAV.ModelItem modelItem) => ResolveGeometryLeafNodes(modelItem);
}
