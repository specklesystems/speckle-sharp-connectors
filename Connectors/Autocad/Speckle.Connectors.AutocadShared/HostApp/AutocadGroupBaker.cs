using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// This resource expects to be injected "fresh" in each send/receive operation (scoped lifetime). Extracts group information from a set of objects into proxies in send operations; also creates groups from a set of proxies in receive operations.
/// </summary>
public class AutocadGroupBaker
{
  private readonly ILogger<AutocadGroupBaker> _logger;
  private readonly AutocadContext _autocadContext;

  public AutocadGroupBaker(AutocadContext autocadContext, ILogger<AutocadGroupBaker> logger)
  {
    _autocadContext = autocadContext;
    _logger = logger;
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

        var groupName = _autocadContext.RemoveInvalidChars(gp.name);
        var newGroup = new Group(groupName, true); // NOTE: this constructor sets both the description (as it says) but also the name at the same time
        newGroup.Append(ids);

        groupDictionary.UpgradeOpen();
        groupDictionary.SetAt(groupName, newGroup);

        groupCreationTransaction.AddNewlyCreatedDBObject(newGroup, true);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        results.Add(new ReceiveConversionResult(Status.ERROR, gp, null, null, e));
        _logger.LogError(e, "Failed to bake Autocad Group."); // TODO: Check with Jedd!
      }
    }

    groupCreationTransaction.Commit();

    return results;
  }
}
