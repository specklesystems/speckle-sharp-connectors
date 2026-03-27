using Microsoft.Extensions.Logging;
using Speckle.Converters.Plant3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts property sets out from a dbobject. Expects to be scoped per operation.
/// </summary>
public class PropertySetExtractor
{
  private readonly PropertySetDefinitionHandler _propertySetDefinitionHandler;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetExtractor> _logger;

  public PropertySetExtractor(
    PropertySetDefinitionHandler propertySetDefinitionHandler,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    ILogger<PropertySetExtractor> logger
  )
  {
    _propertySetDefinitionHandler = propertySetDefinitionHandler;
    _settingsStore = settingsStore;
    _logger = logger;
  }

  /// <summary>
  /// Extracts property sets out from a dbObject. Expects to be scoped per operation.
  /// </summary>
  public Dictionary<string, object?>? GetPropertySets(ADB.DBObject dbObject)
  {
    ADB.ObjectIdCollection? propertySetIds = null;

    try
    {
      propertySetIds = AAECPDB.PropertyDataServices.GetPropertySets(dbObject);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to retrieve property sets on object {HandleValue}", dbObject.Handle.Value);
    }

    if (propertySetIds is null || propertySetIds.Count == 0)
    {
      return null;
    }

    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      Dictionary<string, object?> propertySets = new();
      foreach (ADB.ObjectId id in propertySetIds)
      {
        AAECPDB.PropertySet propertySet = (AAECPDB.PropertySet)tr.GetObject(id, ADB.OpenMode.ForRead);

        if (ParsePropertySet(propertySet, tr) is (string propertySetName, Dictionary<string, object?> propertySetValue))
        {
          propertySets[propertySetName] = propertySetValue;
        }
      }

      tr.Commit();
      return propertySets;
    }
  }

  private (string, Dictionary<string, object?>)? ParsePropertySet(AAECPDB.PropertySet propertySet, ADB.Transaction tr)
  {
    try
    {
      var setDefinition = (AAECPDB.PropertySetDefinition)
        tr.GetObject(propertySet.PropertySetDefinition, ADB.OpenMode.ForRead);
      Dictionary<int, string>? propertyDefinitionNames = null;
      string name = setDefinition.Name;

      propertyDefinitionNames = _propertySetDefinitionHandler.HandleDefinition(setDefinition);

      // get all property values in the propertyset
      Dictionary<string, object?> propertySetData = new();
      foreach (AAECPDB.PropertySetData data in propertySet.PropertySetData)
      {
        string dataName =
          propertyDefinitionNames is not null
          && propertyDefinitionNames.TryGetValue(data.Id, out string? propertyDefinitionName)
            ? propertyDefinitionName
            : data.FieldBucketId;

        var value = data.DataType is AAEC.PropertyData.DataType.Graphic ? null : data.GetData(data.UnitType);

        Dictionary<string, object?> propertyValueDict = new() { ["value"] = value, ["name"] = dataName };
        PropertyHandler propHandler = new();
        propHandler.TryAddToDictionary(propertyValueDict, "units", () => data.UnitType.GetTypeDisplayName(true));

        propertySetData[dataName] = propertyValueDict;
      }

      return (name, propertySetData);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to convert property set {propertySetName}", propertySet.Name);
    }

    return null;
  }
}
