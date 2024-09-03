using Rhino;
using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Unpacks the group lists for each object and sub-objects.
/// POC: Split me into group unpacker and group baker classes please!
/// It should be in scoped lifetime.
/// </summary>
public class RhinoGroupManager // POC: later make it more clean with RhinoGroupUnpacker Packer??? + see same POC comments in instance managers
{
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;

  public RhinoGroupManager(IConversionContextStack<RhinoDoc, UnitSystem> contextStack)
  {
    _settingsStore = settingsStore;
  }

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

  public void BakeGroups(
    List<GroupProxy> groupProxies,
    Dictionary<string, List<string>> applicationIdMap,
    string baseLayerName
  )
  {
    using var _ = SpeckleActivityFactory.Start();
    foreach (GroupProxy groupProxy in groupProxies.OrderBy(g => g.objects.Count))
    {
      var appIds = groupProxy.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]).Select(id => new Guid(id));
      var groupName = (groupProxy.name ?? "No Name Group") + $" ({baseLayerName})";
      _contextStack.Current.Document.Groups.Add(groupName, appIds);
    }
  }

  public void PurgeGroups(string baseLayerName)
  {
    for (int i = _contextStack.Current.Document.Groups.Count; i >= 0; i--)
    {
      var group = _contextStack.Current.Document.Groups.FindIndex(i);
      if (group is { Name: not null } && group.Name.Contains(baseLayerName))
      {
        _contextStack.Current.Document.Groups.Delete(i);
      }
    }
  }
}
