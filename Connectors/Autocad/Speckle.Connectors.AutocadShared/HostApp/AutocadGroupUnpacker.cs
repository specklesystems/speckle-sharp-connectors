using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Core.Models.Instances;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Unpacks a selection of atomic objects into their groups. This resource expects to be injected "fresh" in each send operation (scoped lifetime).
/// </summary>
public class AutocadGroupUnpacker
{
  public List<GroupProxy> UnpackGroups(IEnumerable<AutocadRootObject> autocadObjects)
  {
    var groupProxies = new Dictionary<string, GroupProxy>();

    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    foreach (var (entity, applicationId) in autocadObjects)
    {
      var persistentReactorIds = entity.GetPersistentReactorIds();
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
}
