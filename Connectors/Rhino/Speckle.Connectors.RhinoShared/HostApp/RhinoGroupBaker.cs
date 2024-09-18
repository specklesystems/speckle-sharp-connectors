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

  public RhinoGroupBaker(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<RhinoGroupBaker> logger
  )
  {
    _converterSettings = converterSettings;
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
        _converterSettings.Current.Document.Groups.Add(groupName, appIds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to bake Rhino Group");
      }
    }
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
