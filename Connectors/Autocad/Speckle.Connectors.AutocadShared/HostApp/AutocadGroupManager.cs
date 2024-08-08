using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Core.Logging;
using Speckle.Core.Models.Proxies;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// This resource expects to be injected "fresh" in each send/receive operation (scoped lifetime). Extracts group information from a set of objects into proxies in send operations; also creates groups from a set of proxies in receive operations.
/// </summary>
public class AutocadGroupManager
{
  /// <summary>
  /// Unpacks a selection of atomic objects into their groups
  /// </summary>
  /// <param name="autocadObjects"></param>
  /// <returns></returns>
  public List<GroupProxy> UnpackGroups(IEnumerable<AutocadRootObject> autocadObjects)
  {
    var groupProxies = new Dictionary<string, GroupProxy>();

    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    foreach (var (dbObject, applicationId) in autocadObjects)
    {
      var persistentReactorIds = dbObject.GetPersistentReactorIds();
      foreach (ObjectId oReactorId in persistentReactorIds)
      {
        var obj = transaction.GetObject(oReactorId, OpenMode.ForRead);
        if (obj is not Group group)
        {
          continue;
        }
        var groupAppId = group.Handle.ToString();
        if (groupProxies.TryGetValue(groupAppId, out GroupProxy groupProxy))
        {
          groupProxy.objects.Add(applicationId);
        }
        else
        {
          groupProxies[groupAppId] = new()
          {
            applicationId = groupAppId,
            name = group.Name,
            objects = [applicationId]
          };
        }
      }
    }

    return groupProxies.Values.ToList();
  }

  /// <summary>
  /// Creates groups in the host app from a set of group proxies. Can be called after the bake operation of all atomic objects (including instances) is complete.
  /// </summary>
  /// <param name="groupProxies"></param>
  /// <param name="applicationIdMap"></param>
  /// <returns></returns>
  public List<ReceiveConversionResult> CreateGroups(
    IEnumerable<GroupProxy> groupProxies,
    Dictionary<string, List<Entity>> applicationIdMap
  )
  {
    List<ReceiveConversionResult> results = new();

    using var groupCreationTransaction =
      Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    var groupDictionary = (DBDictionary)
      groupCreationTransaction.GetObject(
        Application.DocumentManager.CurrentDocument.Database.GroupDictionaryId,
        OpenMode.ForWrite
      );

    foreach (var gp in groupProxies.OrderBy(group => group.objects.Count))
    {
      try
      {
        var entities = gp.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]);
        var ids = new ObjectIdCollection();

        foreach (var entity in entities)
        {
          ids.Add(entity.ObjectId);
        }

        var newGroup = new Group(gp.name, true); // NOTE: this constructor sets both the description (as it says) but also the name at the same time
        newGroup.Append(ids);

        groupDictionary.UpgradeOpen();
        groupDictionary.SetAt(gp.name, newGroup);

        groupCreationTransaction.AddNewlyCreatedDBObject(newGroup, true);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        results.Add(new ReceiveConversionResult(Status.ERROR, gp, null, null, e));
      }
    }

    groupCreationTransaction.Commit();

    return results;
  }
}
