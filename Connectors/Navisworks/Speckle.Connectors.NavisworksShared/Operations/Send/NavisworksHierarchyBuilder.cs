using Speckle.Connector.Navisworks.Services;
using Speckle.Converter.Navisworks.Paths;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connector.Navisworks.Operations.Send;

/// <summary>
/// Rebuilds the Navisworks document hierarchy from converted geometry leaves while preserving
/// the parent-child relationships between elements in the original model structure.
/// </summary>
public class NavisworksHierarchyBuilder
{
  private readonly Dictionary<string, Base?> _geometryLeaves;
  private readonly IRootToSpeckleConverter _converter;
  private readonly IElementSelectionService _selectionService;
  private readonly Dictionary<string, Base> _allNodes;

  /// <summary>
  /// Initializes a new instance of the NavisworksHierarchyBuilder.
  /// </summary>
  /// <param name="geometryLeaves">Dictionary of path-indexed converted geometry elements</param>
  /// <param name="converter">Converter to transform Navisworks elements to Speckle objects</param>
  /// <param name="selectionService">Service for resolving Navisworks element paths</param>
  public NavisworksHierarchyBuilder(
    Dictionary<string, Base?> geometryLeaves,
    IRootToSpeckleConverter converter,
    IElementSelectionService selectionService
  )
  {
    _geometryLeaves = geometryLeaves;
    _converter = converter;
    _selectionService = selectionService;
    _allNodes = new Dictionary<string, Base>();
  }

  /// <summary>
  /// Constructs a hierarchical tree of Speckle objects that mirrors the Navisworks document structure.
  /// </summary>
  /// <returns>List of root-level Speckle Base objects containing the full hierarchy</returns>
  public List<Base> BuildHierarchy()
  {
    foreach (var kvp in _geometryLeaves)
    {
      if (kvp.Value != null)
      {
        _allNodes[kvp.Key] = kvp.Value;
      }
    }

    // For each leaf path, traverse up the document structure converting any missing ancestors
    foreach (var nodePath in _allNodes.ToList().Select(kvp => kvp.Key))
    {
      ClimbUpToRoot(nodePath);
    }

    var allPaths = _allNodes.Keys.ToList();
    allPaths.Sort(
      (a, b) =>
      {
        var depthA = a.Count(c => c == PathConstants.SEPARATOR);
        var depthB = b.Count(c => c == PathConstants.SEPARATOR);
        return depthB.CompareTo(depthA); // <- Sort in ascending order of path length
      }
    );

    // Link nodes to parents and identify root nodes that have no recognized parent
    var rootCandidates = new Dictionary<string, Base>(_allNodes);

    foreach (var nodePath in allPaths)
    {
      if (nodePath == "0")
      {
        continue;
      }

      var nodeBase = _allNodes[nodePath];
      var parentPath = GetParentPath(nodePath);

      if (string.IsNullOrEmpty(parentPath))
      {
        continue;
      }

      // Navisworks API: Add child elements to parent collections
      if (!_allNodes.TryGetValue(parentPath, out var parentBase) || parentBase is not Collection parentCollection)
      {
        continue;
      }

      parentCollection.elements.Add(nodeBase);
      rootCandidates.Remove(nodePath);
    }

    var rootNodes = rootCandidates.Values.ToList();
    PruneEmptyCollections(rootNodes);

    return rootNodes;
  }

  private void ClimbUpToRoot(string currentPath)
  {
    while (!string.IsNullOrEmpty(currentPath) && currentPath != "0")
    {
      var parentPath = GetParentPath(currentPath);

      if (string.IsNullOrEmpty(parentPath))
      {
        return;
      }

      if (_allNodes.ContainsKey(parentPath))
      {
        currentPath = parentPath;
        continue;
      }

      var parentModelItem = _selectionService.GetModelItemFromPath(parentPath);
      var parentConverted = _converter.Convert(parentModelItem);
      _allNodes[parentPath] = parentConverted;

      currentPath = parentPath;
    }
  }

  private static string GetParentPath(string path)
  {
    var idx = path.LastIndexOf(PathConstants.SEPARATOR);
    return idx == -1 ? string.Empty : path[..idx];
  }

  private static void PruneEmptyCollections(List<Base> nodes)
  {
    foreach (var node in nodes.ToList())
    {
      if (node is not Collection collection)
      {
        continue;
      }

      PruneEmptyCollections(collection.elements);
      collection.elements.RemoveAll(child => child is Collection { elements.Count: 0 });
    }
  }
}
