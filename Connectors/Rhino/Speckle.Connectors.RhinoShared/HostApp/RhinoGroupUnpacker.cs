using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoGroupUnpacker
{
  private readonly ILogger<RhinoGroupUnpacker> _logger;

  public RhinoGroupUnpacker(ILogger<RhinoGroupUnpacker> logger)
  {
    _logger = logger;
  }

  public Dictionary<string, GroupProxy> GroupProxies { get; } = new();

  public void UnpackGroups(IReadOnlyList<RhinoObject> rhinoObjects)
  {
    if (RhinoDoc.ActiveDoc.Groups.Count == 0)
    {
      // Documents with lots of instances (e.g. skp imports) perform poorly with this function
      // Because its implementation is not very efficient, requiring it to deeply traverse all instances
      // This causes a LOT of memory allocations, which was causing SKP file imports to fail (OOM)
      // This is a very dumb work around. The ideal fix would be for us to optimise this function,
      // and if possible, avoid needing to traverse instances (maybe we could loop over the groups and use `GroupTable.GetMembers(index)` instead)
      return;
    }

    foreach (RhinoObject rhinoObject in rhinoObjects)
    {
      try
      {
        if (rhinoObject is InstanceObject instanceObject)
        {
          UnpackGroups(instanceObject.GetSubObjects());
        }
        var groupList = rhinoObject.GetGroupList();
        if (groupList is null)
        {
          continue;
        }
        var groups = groupList.Select(gi => RhinoDoc.ActiveDoc.Groups.FindIndex(gi));
        foreach (Group group in groups)
        {
          if (GroupProxies.TryGetValue(group.Id.ToString(), out GroupProxy? groupProxy))
          {
            groupProxy.objects.Add(rhinoObject.Id.ToString());
          }
          else
          {
            GroupProxies[group.Id.ToString()] = new GroupProxy()
            {
              applicationId = group.Id.ToString(),
              name = group.Name,
              objects = [rhinoObject.Id.ToString()]
            };
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed on unpacking Rhino group");
      }
    }
  }
}
