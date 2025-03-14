using Speckle.InterfaceGenerator;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Connector.Navisworks.Services;

[GenerateAutoInterface]
public class ElementSelectionService : IElementSelectionService
{
  public string GetModelItemPath(NAV.ModelItem modelItem) => ResolveModelItemToIndexPath(modelItem);

  public NAV.ModelItem GetModelItemFromPath(string path) => ResolveIndexPathToModelItem(path);

  public bool IsVisible(NAV.ModelItem modelItem) => IsElementVisible(modelItem);

  public IEnumerable<NAV.ModelItem> GetGeometryNodes(NAV.ModelItem modelItem) => ResolveGeometryLeafNodes(modelItem);
}
