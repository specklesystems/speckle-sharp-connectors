using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Civil3dShared;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Civil3dShared.ToSpeckle;
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
  private const string PROP_SET_DEF_DICT_NAME = "AecPropertySetDefs";
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
  /// Removes all property set definitions with a prefix before receive operation.
  /// </summary>
  public void PurgePropertySets(string namePrefix)
  {
    ADB.Database db = _settingsStore.Current.Document.Database;
    using var tr = db.TransactionManager.StartTransaction();

    List<ADB.ObjectId> definitionsToDelete = new();

    // Access the property set definition dictionary from the named object dictionary
    var nod = (ADB.DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, ADB.OpenMode.ForRead);

    if (nod.Contains(PROP_SET_DEF_DICT_NAME))
    {
      ADB.ObjectId propSetDefsDictId = nod.GetAt(PROP_SET_DEF_DICT_NAME);
      var propSetDefsDict = (ADB.DBDictionary)tr.GetObject(propSetDefsDictId, ADB.OpenMode.ForRead);

      // Iterate through all property set definitions in the dictionary
      foreach (ADB.DBDictionaryEntry entry in propSetDefsDict)
      {
        if (entry.Key.Contains(namePrefix))
        {
          definitionsToDelete.Add(entry.Value);
        }
      }
    }

    // Delete the matching definitions
    foreach (ADB.ObjectId defId in definitionsToDelete)
    {
      try
      {
        var propSetDef = (AAECPDB.PropertySetDefinition)tr.GetObject(defId, ADB.OpenMode.ForWrite);
        propSetDef.Erase();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Failed to purge property set definition");
      }
    }

    tr.Commit();
  }

  /// <summary>
  /// Parse and bake all property set definitions from the root object.
  /// Should be called after purging and after materials/colors are parsed.
  /// </summary>
  public void ParseAndBakePropertySetDefinitions(Base rootObject, string namePrefix)
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

      if (!setDefData.TryGetValue(PropertySetDefinitionHandler.PROP_SET_PROP_DEFS_KEY, out var propDefsObj))
      {
        _logger.LogWarning("Property set definition {SetName} missing propertyDefinitions", setName);
        continue;
      }

      if (propDefsObj is not Dictionary<string, object?> propertyDefinitions)
      {
        _logger.LogWarning("Property set definition {SetName} propertyDefinitions has invalid format", setName);
        continue;
      }

      ADB.ObjectId defId = CreatePropertySetDefinition(setName, propertyDefinitions, namePrefix, tr);
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

  private ADB.ObjectId CreatePropertySetDefinition(
    string setName,
    Dictionary<string, object?> propertyDefinitions,
    string namePrefix,
    ADB.Transaction tr
  )
  {
    var db = _settingsStore.Current.Document.Database;
    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(db);

    string prefixedName = $"{setName}-{namePrefix}";

    AAECPDB.PropertySetDefinition propSetDef = new();
    propSetDef.SetToStandard(db);
    propSetDef.SubSetDatabaseDefaults(db);
    //propSetDef.Description = "Property Set Definition added by Speckle"; // POC: should use the description that was published. can this back in if needed
    propSetDef.AppliesToAll = true;

    foreach (var propertyDefinition in propertyDefinitions)
    {
      string propertyName = propertyDefinition.Key;
      object? propertyDefObj = propertyDefinition.Value;

      if (propertyDefObj is not Dictionary<string, object?> propertyDefDict)
      {
        continue;
      }

      if (
        !propertyDefDict.TryGetValue(PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY, out var dataTypeStr)
        || dataTypeStr is not string dataTypeString
      )
      {
        _logger.LogError(
          "Property set definition {SetName} is invalid: property {PropertyName} missing or invalid dataType",
          setName,
          propertyName
        );
        return ADB.ObjectId.Null;
      }

      if (!Enum.TryParse(dataTypeString, out AAEC.PropertyData.DataType dataType))
      {
        _logger.LogError(
          "Property set definition {SetName} is invalid: unsupported data type {DataType} for property {PropertyName}",
          setName,
          dataTypeString,
          propertyName
        );
        return ADB.ObjectId.Null;
      }

      AAECPDB.PropertyDefinition propDef = new() { DataType = dataType, Name = propertyName };

      propDef.SetToStandard(db);
      propDef.SubSetDatabaseDefaults(db);

      if (
        propertyDefDict.TryGetValue(PropertySetDefinitionHandler.PROP_DEF_DEFAULT_VALUE_KEY, out object? defaultValue)
        && defaultValue != null
      )
      {
        try
        {
          // Cast numeric types to avoid bad numeric value errors
          var convertedValue = dataType switch
          {
            AAEC.PropertyData.DataType.Integer => (int)(long)defaultValue,
            AAEC.PropertyData.DataType.AutoIncrement => (int)(long)defaultValue,
            _ => defaultValue,
          };

          propDef.DefaultData = convertedValue;
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

    propSetDefs.AddNewRecord(prefixedName, propSetDef);
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

      return TrySetPropertyValues(entity, propertySetDefId, setData, tr);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to add property set to entity");
      return false;
    }
  }

  private bool TrySetPropertyValues(
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
}
