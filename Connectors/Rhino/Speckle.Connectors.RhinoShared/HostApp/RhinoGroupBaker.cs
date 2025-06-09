using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoGroupBaker
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly ILogger<RhinoGroupBaker> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public RhinoGroupBaker(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<RhinoGroupBaker> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
    _activityFactory = activityFactory;
  }

  public void BakeGroups(
    IReadOnlyCollection<GroupProxy> groupProxies,
    Dictionary<string, IReadOnlyCollection<string>> applicationIdMap,
    string baseLayerName
  )
  {
    using var _ = _activityFactory.Start();
    foreach (GroupProxy groupProxy in groupProxies.OrderBy(g => g.objects.Count))
    {
      try
      {
        var groupName = (groupProxy.name ?? "No Name Group") + $" ({baseLayerName})";
        var appIds = groupProxy
          .objects.SelectMany(oldObjId => LookupApplicationIds(groupName, oldObjId, applicationIdMap))
          .Select(id => new Guid(id));
        _converterSettings.Current.Document.Groups.Add(groupName, appIds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to bake Rhino Group");
      }
    }
  }

  private IEnumerable<string> LookupApplicationIds(
    string name,
    string oldObjId,
    Dictionary<string, IReadOnlyCollection<string>> applicationIdMap
  )
  {
    if (applicationIdMap.TryGetValue(oldObjId, out IReadOnlyCollection<string> value))
    {
      return value;
    }
    _logger.LogWarning("Group {group} references an application Id {appId} that cannot be mapped", name, oldObjId);
    return [];
  }

  public void PurgeGroups(string baseLayerName)
  {
    for (int i = _converterSettings.Current.Document.Groups.Count; i >= 0; i--)
    {
      try
      {
        var group = _converterSettings.Current.Document.Groups.FindIndex(i);
        if (group is { Name: not null } && group.Name.Contains(baseLayerName))
        {
          _converterSettings.Current.Document.Groups.Delete(i);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to purge Rhino Group");
      }
    }
  }
}
