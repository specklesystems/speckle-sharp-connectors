using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Connector.Navisworks.Operations.Send.Filters;

public static class SavedItemHelpers
{
  internal static string BuildHierarchicalName(NAV.SavedItem item, NAV.FolderItem? root)
  {
    var pathParts = new List<string> { item.DisplayName };

    var current = item.Parent;
    while (current != null && current != root)
    {
      pathParts.Insert(0, current.DisplayName);
      current = current.Parent;
    }

    return string.Join(PathConstants.SET_SEPARATOR, pathParts);
  }
}
