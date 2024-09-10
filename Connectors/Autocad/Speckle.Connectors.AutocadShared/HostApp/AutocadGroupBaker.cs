using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// This resource expects to be injected "fresh" in each receive operation (scoped lifetime).
/// Extracts group information from a set of objects into proxies in send operations; also creates groups from a set of proxies in receive operations.
/// TODO: Oguzhan! Check whats happening on second receive unless purge groups? naming etc..
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
  // TODO: Oguzhan! Do not report here too! But this is TBD that we don't know the shape of the report yet.
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
      catch (Exception ex) when (!ex.IsFatal())
      {
        results.Add(new ReceiveConversionResult(Status.ERROR, gp, null, null, ex));
        _logger.LogError(ex, "Failed to bake Autocad Group."); // TODO: Check with Jedd!
      }
    }

    groupCreationTransaction.Commit();

    return results;
  }
}
