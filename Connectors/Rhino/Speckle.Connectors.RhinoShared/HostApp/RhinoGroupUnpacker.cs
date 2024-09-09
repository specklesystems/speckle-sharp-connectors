﻿using Rhino;
using Rhino.DocObjects;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoGroupUnpacker
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
  }
}
