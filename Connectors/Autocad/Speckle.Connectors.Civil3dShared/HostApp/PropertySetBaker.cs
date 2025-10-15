using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Civil3dShared;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using AAEC = Autodesk.Aec;
using AAECPDB = Autodesk.Aec.PropertyData.DatabaseServices;
using ADB = Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Civil3dShared.HostApp;

/// <summary>
/// Helper class to bake property sets to entities on receive.
/// </summary>
public class PropertySetBaker
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetBaker> _logger;
  private readonly PropertyHandler _propertyHandler;

  /// <summary>
  /// Map of property set definition name to its ObjectId. Populated during ParsePropertySetDefinitions.
  /// </summary>
  private readonly Dictionary<string, ADB.ObjectId> _propertySetDefinitionMap = new();

  public PropertySetBaker(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ILogger<PropertySetBaker> logger
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
    _propertyHandler = new PropertyHandler();
  }

  /// <summary>
  /// Parse and bake all property set definitions from the root object.
  /// Should be called once at the beginning of the receive operation.
  /// </summary>
  public void ParsePropertySetDefinitions(Base rootObject)
  {
    _propertySetDefinitionMap.Clear();

    if (rootObject[ProxyKeys.PROPERTYSET_DEFINITIONS] is not Dictionary<string, object?> definitions)
    {
      return;
    }

    if (definitions.Count == 0)
    {
      return;
    }

    using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();

    foreach (var definition in definitions)
    {
      string setName = definition.Key;
      object? setDefObj = definition.Value;

      if (setDefObj is not Dictionary<string, object?> setDefData)
      {
        _logger.LogWarning("Property set definition {SetName} has invalid data format", setName);
        continue;
      }

      if (!setDefData.TryGetValue("propertyDefinitions", out var propDefsObj))
      {
        _logger.LogWarning("Property set definition {SetName} missing propertyDefinitions", setName);
        continue;
      }

      if (propDefsObj is not Dictionary<string, object?> propertyDefinitions)
      {
        _logger.LogWarning("Property set definition {SetName} propertyDefinitions has invalid format", setName);
        continue;
      }

      ADB.ObjectId defId = GetOrCreatePropertySetDefinition(setName, propertyDefinitions, tr);
      if (!defId.IsNull)
      {
        _propertySetDefinitionMap[setName] = defId;
      }
    }

    tr.Commit();
  }

  /// <summary>
  /// Try to bake property sets from a Speckle object to a Civil3D entity.
  /// </summary>
  public bool TryBakePropertySets(ADB.Entity entity, Base sourceObject, ADB.Transaction tr)
  {
    if (
      sourceObject["properties"] is not Dictionary<string, object?> properties
      || !properties.TryGetValue("Property Sets", out var propertySetsObj)
      || propertySetsObj is not Dictionary<string, object?> propertySets
      || propertySets.Count == 0
    )
    {
      return false;
    }

    try
    {
      foreach (var propertySet in propertySets)
      {
        string setName = propertySet.Key;
        object? setDataObj = propertySet.Value;

        if (setDataObj is not Dictionary<string, object?> setData)
        {
          _logger.LogWarning("Property set {SetName} has invalid data format", setName);
          continue;
        }

        if (!TryBakePropertySet(entity, setName, setData, tr))
        {
          _logger.LogWarning("Failed to bake property set {SetName} onto entity", setName);
        }
      }

      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to bake property sets onto entity {Handle}", entity.Handle);
      return false;
    }
  }

  private bool TryBakePropertySet(
    ADB.Entity entity,
    string setName,
    Dictionary<string, object?> setData,
    ADB.Transaction tr
  )
  {
    try
    {
      if (!_propertySetDefinitionMap.TryGetValue(setName, out ADB.ObjectId propertySetDefId))
      {
        _logger.LogWarning("Property set definition {SetName} not found in definition map", setName);
        return false;
      }

      if (propertySetDefId.IsNull)
      {
        return false;
      }

      if (ObjectHasPropertySet(entity, propertySetDefId))
      {
        throw new SpeckleException($"Property set '{setName}' already exists on entity.");
      }

      return AddPropertySetToEntity(entity, propertySetDefId, setData, tr);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to process property set {SetName}", setName);
      return false;
    }
  }

  private ADB.ObjectId GetOrCreatePropertySetDefinition(
    string setName,
    Dictionary<string, object?> propertyDefinitions,
    ADB.Transaction tr
  )
  {
    var db = _settingsStore.Current.Document.Database;
    using var propSetDefs = new AAECPDB.DictionaryPropertySetDefinitions(db);

    if (propSetDefs.Has(setName, tr))
    {
      return propSetDefs.GetAt(setName);
    }

    AAECPDB.PropertySetDefinition propSetDef = new();
    propSetDef.SetToStandard(db);
    propSetDef.SubSetDatabaseDefaults(db);
    propSetDef.Description = "Property Set Definition added by Speckle";
    propSetDef.AppliesToAll = true;

    foreach (var propertyDefinition in propertyDefinitions)
    {
      string propertyName = propertyDefinition.Key;
      object? propertyDefObj = propertyDefinition.Value;

      if (propertyDefObj is not Dictionary<string, object?> propertyDefDict)
      {
        continue;
      }

      if (!propertyDefDict.TryGetValue("dataType", out var dataTypeStr) || dataTypeStr is not string dataTypeString)
      {
        _logger.LogWarning("Property {PropertyName} missing or invalid dataType", propertyName);
        continue;
      }

      if (!TryParseDataType(dataTypeString, out AAEC.PropertyData.DataType dataType))
      {
        _logger.LogWarning(
          "Unsupported property data type {DataType} for {PropertyName}",
          dataTypeString,
          propertyName
        );
        continue;
      }

      var propDef = new AAECPDB.PropertyDefinition { DataType = dataType, Name = propertyName };

      propDef.SetToStandard(db);
      propDef.SubSetDatabaseDefaults(db);

      if (propertyDefDict.TryGetValue("defaultValue", out var defaultValue) && defaultValue != null)
      {
        try
        {
          object? convertedValue = ConvertDefaultValue(defaultValue, dataType);
          if (convertedValue != null)
          {
            propDef.DefaultData = convertedValue;
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogWarning(
            ex,
            "Failed to set default value for property {PropertyName}, continuing without default",
            propertyName
          );
        }
      }

      propSetDef.Definitions.Add(propDef);
    }

    propSetDefs.AddNewRecord(setName, propSetDef);
    tr.AddNewlyCreatedDBObject(propSetDef, true);

    return propSetDef.ObjectId;
  }

  private bool ObjectHasPropertySet(ADB.DBObject obj, ADB.ObjectId propertySetId)
  {
    try
    {
      ADB.ObjectId tempId = AAECPDB.PropertyDataServices.GetPropertySet(obj, propertySetId);
      return !tempId.IsNull;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception ex) when (!ex.IsFatal())
    {
      return false;
    }
  }

  private bool AddPropertySetToEntity(
    ADB.Entity entity,
    ADB.ObjectId propertySetDefId,
    Dictionary<string, object?> setData,
    ADB.Transaction tr
  )
  {
    try
    {
      if (!entity.IsWriteEnabled)
      {
        entity.UpgradeOpen();
      }

      AAECPDB.PropertyDataServices.AddPropertySet(entity, propertySetDefId);

      return UpdatePropertySet(entity, propertySetDefId, setData, tr);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to add property set to entity");
      return false;
    }
  }

  private bool UpdatePropertySet(
    ADB.Entity entity,
    ADB.ObjectId propertySetDefId,
    Dictionary<string, object?> setData,
    ADB.Transaction tr
  )
  {
    try
    {
      ADB.ObjectId propertySetId = AAECPDB.PropertyDataServices.GetPropertySet(entity, propertySetDefId);
      var propertySet = (AAECPDB.PropertySet)tr.GetObject(propertySetId, ADB.OpenMode.ForWrite);
      var setDefinition = (AAECPDB.PropertySetDefinition)tr.GetObject(propertySetDefId, ADB.OpenMode.ForRead);

      // Build a map of property names to definition IDs
      Dictionary<string, int> propertyNameToId = new();
      foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
      {
        propertyNameToId[propDef.Name] = propDef.Id;
      }

      foreach (var propertyEntry in setData)
      {
        string propertyName = propertyEntry.Key;
        object? propertyDataObj = propertyEntry.Value;

        if (propertyDataObj is not Dictionary<string, object?> propertyDataDict)
        {
          continue;
        }

        if (!propertyDataDict.TryGetValue("value", out var value) || value == null)
        {
          continue;
        }

        if (!propertyNameToId.TryGetValue(propertyName, out int propertyId))
        {
          continue;
        }

        _propertyHandler.TryGetValue(
          () =>
          {
            propertySet.SetAt(propertyId, value);
            return true;
          },
          out _
        );
      }

      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to update property set values");
      return false;
    }
  }

  private bool TryParseDataType(string dataTypeString, out AAEC.PropertyData.DataType dataType)
  {
    return Enum.TryParse(dataTypeString, out dataType);
  }

  private object? ConvertDefaultValue(object value, AAEC.PropertyData.DataType dataType)
  {
    try
    {
      return dataType switch
      {
        AAEC.PropertyData.DataType.Integer => Convert.ToInt32(value),
        AAEC.PropertyData.DataType.Real => Convert.ToDouble(value),
        AAEC.PropertyData.DataType.TrueFalse => Convert.ToBoolean(value),
        AAEC.PropertyData.DataType.Text => value.ToString(),
        _ => value
      };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to convert default value {Value} to type {DataType}", value, dataType);
      return null;
    }
  }
}
