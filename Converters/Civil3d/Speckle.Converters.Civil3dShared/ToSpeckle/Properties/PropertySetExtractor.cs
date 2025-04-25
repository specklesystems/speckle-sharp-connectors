using Microsoft.Extensions.Logging;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts property sets out from a dbobject. Expects to be scoped per operation.
/// </summary>
public class PropertySetExtractor
{
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  private readonly PropertySetDefinitionHandler _propertySetDefinitionHandler;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetExtractor> _logger;

  public PropertySetExtractor(
    PropertySetDefinitionHandler propertySetDefinitionHandler,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
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
  /// <param name="dbObject"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetPropertySets(ADB.DBObject dbObject)
  {
    ADB.ObjectIdCollection? propertySetIds = null;

    try
    {
      propertySetIds = AAECPDB.PropertyDataServices.GetPropertySets(dbObject);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to retrieve property sets on object {dbObject.Handle.Value}");
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

        // parse property sets within this transaction, since we'll need it for retrieving the definition as well
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
      // var isNullOrEmpty = value == null || (value is string s && string.IsNullOrEmpty(s));
      // POC: should add same check as in revit for sending null or empty values
      var setDefinition = (AAECPDB.PropertySetDefinition)
        tr.GetObject(propertySet.PropertySetDefinition, ADB.OpenMode.ForRead);
      Dictionary<int, string>? propertyDefinitionNames = null;
      string name = setDefinition.Name;

      propertyDefinitionNames = _propertySetDefinitionHandler.HandleDefinition(setDefinition);

      // get all property values in the propertyset
      Dictionary<string, object?> properties = new();
      foreach (AAECPDB.PropertySetData data in propertySet.PropertySetData)
      {
        string dataName =
          propertyDefinitionNames is not null
          && propertyDefinitionNames.TryGetValue(data.Id, out string? propertyDefinitionName)
            ? propertyDefinitionName
            : data.FieldBucketId;

        var value = GetValue(data);

        Dictionary<string, object?> propertyValueDict = new() { ["value"] = value, ["name"] = dataName };
        PropertyHandler propHandler = new();
        propHandler.TryAddToDictionary(propertyValueDict, "units", () => data.UnitType.GetTypeDisplayName(true)); // units not always applicable to def, will throw

        properties[dataName] = propertyValueDict;
      }

      // add property set to dict
      Dictionary<string, object?> propertySetDict =
        new()
        {
          ["name"] = name,
          ["properties"] = properties,
          ["definitionName"] = name
        };

      return (name, propertySetDict);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to convert property set {propertySet.Name}");
    }

    return null;
  }

  private object? GetValue(AAECPDB.PropertySetData data)
  {
    object fieldData = data.GetData(data.UnitType);

    switch (data.DataType)
    {
      case AAEC.PropertyData.DataType.Integer:
        return fieldData as int?;
      case AAEC.PropertyData.DataType.Real:
        return fieldData as double?;
      case AAEC.PropertyData.DataType.TrueFalse:
        return fieldData as bool?;
      case AAEC.PropertyData.DataType.Graphic: // POC: not sure how to support atm
        return null;
      case AAEC.PropertyData.DataType.List:
        return fieldData as List<object>;
      case AAEC.PropertyData.DataType.AutoIncrement:
        return fieldData as int?;
      case AAEC.PropertyData.DataType.AlphaIncrement: // POC: not sure what this is
        return fieldData;
      case AAEC.PropertyData.DataType.Text:
        return fieldData as string;
      default:
        return fieldData;
    }
  }
}
