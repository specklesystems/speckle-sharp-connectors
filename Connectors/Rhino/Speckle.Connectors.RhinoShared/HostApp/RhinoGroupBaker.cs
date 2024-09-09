using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoGroupBaker
{
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;
  private readonly ILogger<RhinoGroupBaker> _logger;

  public RhinoGroupBaker(IConversionContextStack<RhinoDoc, UnitSystem> contextStack, ILogger<RhinoGroupBaker> logger)
  {
    _contextStack = contextStack;
    _logger = logger;
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
      try
      {
        var appIds = groupProxy.objects.SelectMany(oldObjId => applicationIdMap[oldObjId]).Select(id => new Guid(id));
        var groupName = (groupProxy.name ?? "No Name Group") + $" ({baseLayerName})";
        _contextStack.Current.Document.Groups.Add(groupName, appIds);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        _logger.LogError(e, "Failed to bake Rhino Group."); // TODO: Check with Jedd!
      }
    }
  }

  public void PurgeGroups(string baseLayerName)
  {
    for (int i = _contextStack.Current.Document.Groups.Count; i >= 0; i--)
    {
      try
      {
        var group = _contextStack.Current.Document.Groups.FindIndex(i);
        if (group is { Name: not null } && group.Name.Contains(baseLayerName))
        {
          _contextStack.Current.Document.Groups.Delete(i);
        }
      }
      catch (Exception e) when (!e.IsFatal())
      {
        _logger.LogError(e, "Failed to purge Rhino Group."); // TODO: Check with Jedd!
      }
    }
  }
}
