using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
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

  public void UnpackGroups(IEnumerable<RhinoObject> rhinoObjects)
  {
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
              objects = [rhinoObject.Id.ToString()],
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
