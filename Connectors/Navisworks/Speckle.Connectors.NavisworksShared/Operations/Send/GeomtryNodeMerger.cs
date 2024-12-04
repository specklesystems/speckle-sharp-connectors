using Speckle.Connector.Navisworks.Extensions;

namespace Speckle.Connector.Navisworks.Operations.Send;

/// <summary>
/// Groups geometry nodes by their parent paths for merging displayValues
/// </summary>
public class GeometryNodeMerger
{
  public Dictionary<string, List<NAV.ModelItem>> GroupSiblingGeometryNodes(IReadOnlyList<NAV.ModelItem> nodes) =>
    nodes
      .GroupBy(node =>
      {
        var path = ElementSelectionExtension.ResolveModelItemToIndexPath(node);
        var lastSeparatorIndex = path.LastIndexOf(PathConstants.SEPARATOR);
        return lastSeparatorIndex == -1 ? path : path[..lastSeparatorIndex];
      })
      .Where(group => group.Count() > 1) // Only group multiples
      .ToDictionary(group => group.Key, group => group.ToList());
}
