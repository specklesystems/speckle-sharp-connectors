using Rhino;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoGroupBaker
{
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;

  public RhinoGroupBaker(IConversionContextStack<RhinoDoc, UnitSystem> contextStack)
  {
    _contextStack = contextStack;
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
