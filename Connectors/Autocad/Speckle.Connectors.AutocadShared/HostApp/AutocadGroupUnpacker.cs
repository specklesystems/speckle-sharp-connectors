using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// This resource expects to be injected "fresh" in each send/receive operation (scoped lifetime). Extracts group information from a set of objects into proxies in send operations; also creates groups from a set of proxies in receive operations.
/// </summary>
public class AutocadGroupUnpacker
{
  private readonly ILogger<AutocadGroupUnpacker> _logger;

  public AutocadGroupUnpacker(ILogger<AutocadGroupUnpacker> logger)
  {
    _logger = logger;
  }

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
      try
      {
        var persistentReactorIds = dbObject.GetPersistentReactorIds();
        foreach (ObjectId oReactorId in persistentReactorIds)
        {
          var obj = transaction.GetObject(oReactorId, OpenMode.ForRead);
          if (obj is not Group group)
          {
            continue;
          }
          var groupAppId = group.GetSpeckleApplicationId();
          if (groupProxies.TryGetValue(groupAppId, out GroupProxy? groupProxy))
          {
            groupProxy.objects.Add(applicationId);
          }
          else
          {
            groupProxies[groupAppId] = new()
            {
              applicationId = groupAppId,
              name = group.Name,
              objects = [applicationId],
            };
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed on unpacking Autocad group");
      }
    }

    return groupProxies.Values.ToList();
  }
}
