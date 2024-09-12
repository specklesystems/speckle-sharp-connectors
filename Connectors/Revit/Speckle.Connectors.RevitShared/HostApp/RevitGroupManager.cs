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
  private readonly RevitUtils _revitUtils;

  public RevitGroupManager(
    IRevitConversionContextStack contextStack,
    ITransactionManager transactionManager,
    RevitUtils revitUtils
  )
  {
    _contextStack = contextStack;
    _transactionManager = transactionManager;
    _revitUtils = revitUtils;
  }

  // We cannot add objects to groups in revit, we need to create a group all at once with all its subkids
  // We need to create groups at the end of the day in a separate new transaction
  public void AddToGroupMapping(TraversalContext tc, DirectShape ds)
  {
    var collectionPath = GetLayerPath(tc);
    var currentLayerName = "Base Group";
    FakeGroup? previousGroup = null;
    var currentDepth = 0;

    foreach (var collection in collectionPath)
    {
      currentLayerName += "::" + collection.name;
      if (_groupCache.TryGetValue(currentLayerName, out var g))
      {
        previousGroup = g;
        currentDepth++;
        continue;
      }

      var group = new FakeGroup()
      {
        Name = _revitUtils.RemoveInvalidChars(collection.name),
        Depth = currentDepth++,
        Parent = previousGroup!
      };
      _groupCache[currentLayerName] = group;
      previousGroup = group;
    }

    previousGroup!.Ids.Add(ds.Id);
  }

  private readonly Dictionary<string, FakeGroup> _groupCache = new();

  public void BakeGroups(string baseGroupName)
  {
    var orderedGroups = _groupCache.Values.OrderByDescending(group => group.Depth);
    Group? lastGroup = null;

    foreach (var group in orderedGroups)
    {
      var docGroup = _contextStack.Current.Document.Create.NewGroup(group.Ids);
      group.Parent?.Ids.Add(docGroup.Id);
      docGroup.GroupType.Name = group.Name;
      lastGroup = docGroup;
    }

    lastGroup!.GroupType.Name = baseGroupName;
  }

  private sealed class FakeGroup
  {
    public List<ElementId> Ids { get; set; } = new();
    public int Depth { get; set; }
    public string Name { get; set; }
    public FakeGroup Parent { get; set; }
  }
}
