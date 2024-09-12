using Autodesk.Revit.DB;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Revit.HostApp;

public class RevitGroupManager
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

  public List<ReceiveConversionResult> CreateGroups(
    IEnumerable<GroupProxy> groupProxies,
    Dictionary<string, List<string>> applicationIdMap
  )
  {
    List<ReceiveConversionResult> results = new();

    using TransactionGroup createGroupTransaction = new(_contextStack.Current.Document, "Creating Revit group");
    createGroupTransaction.Start();
    _transactionManager.StartTransaction();

    //_contextStack.Current.Document.Create.NewGroup(elementIds);

    foreach (var gp in groupProxies.OrderBy(group => group.objects.Count))
    {
      try
      {
        var entities = gp.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]);
        //var appIds = gp.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]).Select(id => new Guid(id));
        //var ids = new ObjectIdCollection();
        var ids = new List<ElementId>();

        foreach (var entity in entities) { }

        //var groupName = _autocadContext.RemoveInvalidChars(gp.name);
        //var newGroup = new Group(groupName, true);
        //newGroup.Append(ids);
        //groupDictionary.UpgradeOpen();
        //groupDictionary.SetAt(groupName, newGroup);
        //groupCreationTransaction.AddNewlyCreatedDBObject(newGroup, true);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        results.Add(new ReceiveConversionResult(Status.ERROR, gp, null, null, e));
      }
    }

    using (var _ = SpeckleActivityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      createGroupTransaction.Assimilate();
    }

    return results;
  }
}
