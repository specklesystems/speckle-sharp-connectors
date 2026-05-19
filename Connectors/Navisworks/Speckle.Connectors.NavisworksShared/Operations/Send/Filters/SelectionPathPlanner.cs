using Speckle.Converter.Navisworks.Helpers;

namespace Speckle.Connector.Navisworks.Operations.Send.Filters;

internal static class SelectionPathPlanner
{
  public static SelectionPathPlan BuildPlan(IEnumerable<string> selectedPaths)
  {
    var cleanedDistinctPaths = selectedPaths
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .Select(ElementSelectionHelper.GetCleanPath)
      .Distinct(StringComparer.Ordinal)
      .OrderBy(path => path.Count(c => c == '.'))
      .ThenBy(path => path, StringComparer.Ordinal)
      .ToList();

    if (cleanedDistinctPaths.Count == 0)
    {
      return new SelectionPathPlan([], 0);
    }

    var rootPaths = new List<string>(cleanedDistinctPaths.Count);
    var acceptedPaths = new HashSet<string>(StringComparer.Ordinal);
    int prunedDescendantCount = 0;

    foreach (var path in cleanedDistinctPaths)
    {
      if (HasSelectedAncestor(path, acceptedPaths))
      {
        prunedDescendantCount++;
        continue;
      }

      acceptedPaths.Add(path);
      rootPaths.Add(path);
    }

    return new SelectionPathPlan(rootPaths, prunedDescendantCount);
  }

  private static bool HasSelectedAncestor(string path, ISet<string> acceptedPaths)
  {
    int separatorIndex = path.LastIndexOf('.');
    while (separatorIndex > 0)
    {
      string ancestor = path[..separatorIndex];
      if (acceptedPaths.Contains(ancestor))
      {
        return true;
      }

      separatorIndex = ancestor.LastIndexOf('.');
    }

    return false;
  }
}

internal readonly record struct SelectionPathPlan(IReadOnlyList<string> RootPaths, int PrunedDescendantCount);
