using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Connector.Navisworks.Services;

public interface IElementSelectionService
{
  string GetModelItemPath(NAV.ModelItem modelItem);
  NAV.ModelItem GetModelItemFromPath(string path);
  bool IsVisible(NAV.ModelItem modelItem);
  IReadOnlyCollection<NAV.ModelItem> GetGeometryNodes(NAV.ModelItem modelItem);
}

public class ElementSelectionService : IElementSelectionService
{
  public string GetModelItemPath(NAV.ModelItem modelItem) => ResolveModelItemToIndexPath(modelItem);

  public NAV.ModelItem GetModelItemFromPath(string path) => ResolveIndexPathToModelItem(path);

  public bool IsVisible(NAV.ModelItem modelItem) => IsElementVisible(modelItem);

  public IReadOnlyCollection<NAV.ModelItem> GetGeometryNodes(NAV.ModelItem modelItem) =>
    ResolveGeometryLeafNodes(modelItem);
}
