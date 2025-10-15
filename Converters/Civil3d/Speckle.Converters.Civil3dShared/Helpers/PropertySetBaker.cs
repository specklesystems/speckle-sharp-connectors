using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>
/// Helper class to bake property sets to entities on receive.
/// </summary>
public class PropertySetBaker
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetBaker> _logger;
  private readonly PropertyHandler _propertyHandler;

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
  /// Try to bake property sets from a Speckle object to an AutoCAD entity.
  /// </summary>
  /// <param name="entity">The target entity.</param>
  /// <param name="sourceObject">The source Speckle object containing property set data.</param>
  public bool TryBakePropertySets(ADB.Entity entity, Base sourceObject)
  {
    if (sourceObject["properties"] is not Dictionary<string, object?> properties)
    {
      return false;
    }

    if (!properties.TryGetValue("Property Sets", out var propertySetsObj))
    {
      return false;
    }

    if (propertySetsObj is not Dictionary<string, object?> propertySets || propertySets.Count == 0)
    {
      return false;
    }

    try
    {
      using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();

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

      tr.Commit();
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
      ADB.ObjectId propertySetDefId = GetOrCreatePropertySetDefinition(setName, setData, tr);

      if (propertySetDefId.IsNull)
      {
        return false;
      }

      // Check if property set already exists on the object
      if (ObjectHasPropertySet(entity, propertySetDefId))
      {
        return UpdatePropertySet(entity, propertySetDefId, setData, tr);
      }
      else
      {
        return AddPropertySetToEntity(entity, propertySetDefId, setData, tr);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to process property set {SetName}", setName);
      return false;
    }
  }

  private ADB.ObjectId GetOrCreatePropertySetDefinition(
    string setName,
    Dictionary<string, object?> setData,
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

      AAEC.PropertyData.DataType? dataType = GetPropertyDataType(value);
      if (dataType == null)
      {
        _logger.LogWarning("Unsupported property data type for {PropertyName}", propertyName);
        continue;
      }

      var propDef = new AAECPDB.PropertyDefinition
      {
        DataType = dataType.Value,
        Name = propertyName,
        DefaultData = value
      };

      propDef.SetToStandard(db);
      propDef.SubSetDatabaseDefaults(db);
      propSetDef.Definitions.Add(propDef);
    }

    propSetDefs.AddNewRecord(setName, propSetDef);
    tr.AddNewlyCreatedDBObject(propSetDef, true);

    ADB.ObjectId defId = propSetDef.ObjectId;

    return defId;
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

  private AAEC.PropertyData.DataType? GetPropertyDataType(object value)
  {
    return value switch
    {
      int => AAEC.PropertyData.DataType.Integer,
      double => AAEC.PropertyData.DataType.Real,
      bool => AAEC.PropertyData.DataType.TrueFalse,
      string => AAEC.PropertyData.DataType.Text,
      List<int> => AAEC.PropertyData.DataType.List,
      List<double> => AAEC.PropertyData.DataType.List,
      List<bool> => AAEC.PropertyData.DataType.List,
      List<string> => AAEC.PropertyData.DataType.List,
      _ => null
    };
  }
}
