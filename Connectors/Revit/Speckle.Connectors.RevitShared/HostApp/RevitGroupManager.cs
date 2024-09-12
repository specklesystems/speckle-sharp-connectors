using Autodesk.Revit.DB;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Utils.Operations.Receive;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.HostApp;

public class RevitGroupManager : LayerPathUnpacker
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ITransactionManager _transactionManager;

  public RevitGroupManager(IRevitConversionContextStack contextStack, ITransactionManager transactionManager)
  {
    _contextStack = contextStack;
    _transactionManager = transactionManager;
  }

  public void CreateGroups()
  {
    // build edge list of the element graph in the bakeobjects method
    // pass the edge list to the GroupManager
    // in the GroupManager CreateGroups method:
    // 1. find leaf nodes
    // 2. group leaf nodes by parent id
    // 3. make revit groups from each leaf group
    // 4. replace the parent id of the leaves with the new group id everywhere in the edge list
    // 5. remove leaf edges
    // 6. repeat the above until we have edges

    return;
  }

  // We cannot add objects to groups in revit, we need to create a group all at once with all its subkids
  // We need to create groups at the end of the day in a separate new transaction
  public void AddToGroupMapping(TraversalContext tc, DirectShape ds)
  {
    var collectionPath = GetLayerPath(tc);
    var currentLayerName = "Base Group";
    var previousGroup = _baseGroup;
    var currentDepth = 0;
    foreach (var collection in collectionPath)
    {
      currentLayerName += "::" + collection.name;
      if (_groupCache.TryGetValue(currentLayerName, out FakeGroup g))
      {
        previousGroup = g;
        continue;
      }

      var group = new FakeGroup()
      {
        Name = currentLayerName,
        Depth = currentDepth++,
        Parent = previousGroup
      };
      _groupCache[currentLayerName] = group;
      previousGroup = group;
    }

    previousGroup.Ids.Add(ds.Id);
  }

  private readonly Dictionary<string, FakeGroup> _groupCache = new();

  public void BakeGroups()
  {
    var orderedGroups = _groupCache.Values.OrderByDescending(group => group.Depth);
    foreach (var group in orderedGroups)
    {
      var docGroup = _contextStack.Current.Document.Create.NewGroup(group.Ids); // TODO
      group.Parent.Ids.Add(docGroup.Id);
    }
  }

  private FakeGroup _baseGroup = new() { Name = "Base Group", Depth = 0 };
}

sealed class FakeGroup
{
  public List<ElementId> Ids { get; set; } = new();
  public int Depth { get; set; }
  public string Name { get; set; }
  public string HostId { get; set; } // will be used on baking, should be subsumed into ids
  public FakeGroup Parent { get; set; }
}
