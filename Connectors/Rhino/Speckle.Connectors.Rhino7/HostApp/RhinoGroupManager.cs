using Rhino;
using Rhino.DocObjects;
using Speckle.Core.Models.Instances;

namespace Speckle.Connectors.Rhino7.HostApp;

/// <summary>
/// Unpacks the group lists for each object and sub-objects.
/// It should be in scoped lifetime.
/// </summary>
public class RhinoGroupManager // POC: later make it more clean with RhinoGroupUnpacker Packer??? + see same POC comments in instance managers
{
  public Dictionary<string, GroupProxy> GroupProxies { get; } = new();

  public void UnpackGroups(IEnumerable<RhinoObject> rhinoObjects)
  {
    foreach (RhinoObject rhinoObject in rhinoObjects)
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
        if (GroupProxies.TryGetValue(group.Id.ToString(), out GroupProxy groupProxy))
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
  }
}
