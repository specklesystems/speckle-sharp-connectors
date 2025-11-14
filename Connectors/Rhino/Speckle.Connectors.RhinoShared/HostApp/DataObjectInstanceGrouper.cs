using Microsoft.Extensions.Logging;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.ToHost;
using Speckle.Converters.Rhino;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Groups block instances created from DataObject with InstanceProxies as display values and applies DataObject metadata.
/// </summary>
public class DataObjectInstanceGrouper
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly ILogger<DataObjectInstanceGrouper> _logger;
  private readonly IDataObjectInstanceRegistry _dataObjectInstanceRegistry;

  public DataObjectInstanceGrouper(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<DataObjectInstanceGrouper> logger,
    IDataObjectInstanceRegistry dataObjectInstanceRegistry
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
    _dataObjectInstanceRegistry = dataObjectInstanceRegistry;
  }

  /// <summary>
  /// After all instances have been created, we then run through the data object instance registry to see which instances
  /// belonged to a data object. The method then groups all instances to "re-assemble" the original data object and
  /// applies the properties of the data object on to the instances.
  /// </summary>
  /// <remarks>
  /// This is a deferred action and can only occur once the RhinoInstanceBaker has done its thing.
  /// </remarks>
  public void GroupAndApplyProperties()
  {
    var doc = _converterSettings.Current.Document;
    var entries = _dataObjectInstanceRegistry.GetEntries(); // see docstring

    foreach (var kvp in entries)
    {
      var dataObjectId = kvp.Key;
      var entry = kvp.Value;
      try
      {
        var instanceIds = _dataObjectInstanceRegistry.GetInstanceIdsForDataObject(dataObjectId);
        if (instanceIds.Count == 0)
        {
          continue;
        }

        // create group, name the group and apply properties
        using var dataObjectAtts = entry.DataObject.GetAttributes();
        var groupName = dataObjectAtts.Name;
        var groupIndex = doc.Groups.Add(groupName, instanceIds.Select(id => new Guid(id)));

        if (groupIndex >= 0)
        {
          // apply properties to each instance (doing this on an instance level because setting to group doesn't work)
          foreach (var instanceId in instanceIds)
          {
            var rhinoObj = doc.Objects.FindId(new Guid(instanceId));
            if (rhinoObj != null)
            {
              // set the name from DataObject
              rhinoObj.Attributes.Name = dataObjectAtts.Name;

              // copy all user strings
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
