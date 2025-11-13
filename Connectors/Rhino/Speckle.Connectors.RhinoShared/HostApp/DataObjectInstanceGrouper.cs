using Microsoft.Extensions.Logging;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.ToHost;
using Speckle.Converters.Rhino;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Groups block instances created from DataObject InstanceProxies and applies DataObject metadata.
/// </summary>
public class DataObjectInstanceGrouper
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly ILogger<DataObjectInstanceGrouper> _logger;

  public DataObjectInstanceGrouper(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<DataObjectInstanceGrouper> logger
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
  }

  /// <summary>
  /// Groups instances belonging to the same DataObject and applies DataObject metadata.
  /// </summary>
  public void GroupAndApplyMetadata(IDataObjectInstanceRegistry registry, string baseLayerName)
  {
    var doc = _converterSettings.Current.Document;
    var entries = registry.GetEntries();

    foreach (var kvp in entries)
    {
      var dataObjectId = kvp.Key;
      var entry = kvp.Value;
      try
      {
        var instanceIds = registry.GetInstanceIdsForDataObject(dataObjectId);
        if (instanceIds.Count == 0)
        {
          continue;
        }

        // Create group
        var groupName = (entry.DataObject["name"] as string ?? "DataObject Group") + $" ({baseLayerName})";
        var groupIndex = doc.Groups.Add(groupName, instanceIds.Select(id => new Guid(id)));

        if (groupIndex >= 0)
        {
          // Apply DataObject metadata to each instance
          using var dataObjectAtts = entry.DataObject.GetAttributes();
          foreach (var instanceId in instanceIds)
          {
            var rhinoObj = doc.Objects.FindId(new Guid(instanceId));
            if (rhinoObj != null)
            {
              foreach (var key in dataObjectAtts.GetUserStrings().AllKeys)
              {
                rhinoObj.Attributes.SetUserString(key, dataObjectAtts.GetUserString(key));
              }
              rhinoObj.CommitChanges();
            }
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to group DataObject instances {dataObjectId}", dataObjectId);
      }
    }
  }
}
