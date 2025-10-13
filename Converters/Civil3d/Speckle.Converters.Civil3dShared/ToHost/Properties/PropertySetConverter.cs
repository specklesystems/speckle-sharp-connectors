using Microsoft.Extensions.Logging;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared.ToHost.Properties;

/// <summary>
/// applies property sets to an object on receive
/// </summary>
public class PropertySetConverter
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetConverter> _logger;
  private readonly Dictionary<string, ADB.ObjectId> _propertySetDefinitionCache = new();

  public PropertySetConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    ILogger<PropertySetConverter> logger
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public void SetPropertySets(ADB.Entity entity, Dictionary<string, object?> properties, ADB.Transaction tr)
  {
    if (properties == null)
    {
      return;
    }

    // check if property sets exist
    if (
      !properties.TryGetValue("Property Sets", out object? propertySetsObj)
      || propertySetsObj is not Dictionary<string, object?> propertySets
    )
    {
      return;
    }

    foreach (KeyValuePair<string, object?> kvp in propertySets)
    {
      string propertySetName = kvp.Key;
      object? propertySetDataObj = kvp.Value;

      if (propertySetDataObj is not Dictionary<string, object?> propertySetData)
      {
        continue;
      }

      try
      {
        ApplyPropertySet(entity, propertySetName, propertySetData, tr);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        _logger.LogWarning(e, $"Failed to apply property set '{propertySetName}'");
      }
    }
  }

  private void ApplyPropertySet(
    ADB.Entity entity,
    string propertySetName,
    Dictionary<string, object?> propertySetData,
    ADB.Transaction tr
  )
  {
    ADB.ObjectId propertySetDefId = GetOrCreatePropertySetDefinition(propertySetName, propertySetData, tr);

    if (propertySetDefId == ADB.ObjectId.Null)
    {
      return;
    }

    // create property set instance on the entity
    AAECPDB.PropertySet propertySet = GetOrCreatePropertySet(entity, propertySetDefId, tr);

    SetPropertyValues(propertySet, propertySetDefId, propertySetData, tr);
  }

  private ADB.ObjectId GetOrCreatePropertySetDefinition(
    string propertySetName,
    Dictionary<string, object?> propertySetData,
    ADB.Transaction tr
  )
  {
    // check cache
    if (_propertySetDefinitionCache.TryGetValue(propertySetName, out ADB.ObjectId cachedId))
    {
      return cachedId;
    }

    // try to find existing definition
    var database = _settingsStore.Current.Document.Database;
    var nod = (ADB.DBDictionary)tr.GetObject(database.NamedObjectsDictionaryId, ADB.OpenMode.ForRead);

    if (!nod.Contains("AecPropertySetDefs"))
    {
      // api not available or no property set definitions exist yet - create the dictionary
      nod.UpgradeOpen();
      var newDefDict = new ADB.DBDictionary();
      nod.SetAt("AecPropertySetDefs", newDefDict);
      tr.AddNewlyCreatedDBObject(newDefDict, true);
      return CreatePropertySetDefinition(propertySetName, propertySetData, tr, newDefDict);
    }

    var defDict = (ADB.DBDictionary)tr.GetObject(nod.GetAt("AecPropertySetDefs"), ADB.OpenMode.ForRead);

    if (defDict.Contains(propertySetName))
    {
      var defId = defDict.GetAt(propertySetName);
      _propertySetDefinitionCache[propertySetName] = defId;
      return defId;
    }

    // create new definition
    return CreatePropertySetDefinition(propertySetName, propertySetData, tr, defDict);
  }

  private ADB.ObjectId CreatePropertySetDefinition(
    string propertySetName,
    Dictionary<string, object?> propertySetData,
    ADB.Transaction tr,
    ADB.DBDictionary defDict
  )
  {
    var propertySetDef = new AAECPDB.PropertySetDefinition();
    propertySetDef.SetToStandard(_settingsStore.Current.Document.Database);
    propertySetDef.Description = $"Received from Speckle";

    // add property definitions
    foreach (KeyValuePair<string, object?> kvp in propertySetData)
    {
      string propertyName = kvp.Key;
      object? propertyValueObj = kvp.Value;

      if (
        propertyValueObj is not Dictionary<string, object?> propertyValueDict
        || !propertyValueDict.TryGetValue("value", out object? value)
      )
      {
        continue;
      }

      var propertyDef = new AAECPDB.PropertyDefinition();
      propertyDef.SetToStandard(_settingsStore.Current.Document.Database);
      propertyDef.Name = propertyName;
      propertyDef.DataType = GetDataTypeFromValue(value);
      propertySetDef.Definitions.Add(propertyDef);
    }

    defDict.UpgradeOpen();
    defDict.SetAt(propertySetName, propertySetDef);
    tr.AddNewlyCreatedDBObject(propertySetDef, true);

    var defId = propertySetDef.ObjectId;
    _propertySetDefinitionCache[propertySetName] = defId;
    return defId;
  }

  private static AAECPDB.PropertySet GetOrCreatePropertySet(
    ADB.Entity entity,
    ADB.ObjectId propertySetDefId,
    ADB.Transaction tr
  )
  {
    // check if property set already attached
    ADB.ObjectIdCollection existingPropertySets = AAECPDB.PropertyDataServices.GetPropertySets(entity);

    foreach (ADB.ObjectId psId in existingPropertySets)
    {
      var ps = (AAECPDB.PropertySet)tr.GetObject(psId, ADB.OpenMode.ForRead);
      if (ps.PropertySetDefinition == propertySetDefId)
      {
        ps.UpgradeOpen();
        return ps;
      }
    }

    // create new property set and attach to entity
    entity.UpgradeOpen();
    AAECPDB.PropertyDataServices.AddPropertySet(entity, propertySetDefId);

    // get the newly attached property set
    existingPropertySets = AAECPDB.PropertyDataServices.GetPropertySets(entity);
    foreach (ADB.ObjectId psId in existingPropertySets)
    {
      var ps = (AAECPDB.PropertySet)tr.GetObject(psId, ADB.OpenMode.ForWrite);
      if (ps.PropertySetDefinition == propertySetDefId)
      {
        return ps;
      }
    }

    throw new InvalidOperationException("Failed to create property set");
  }

  private void SetPropertyValues(
    AAECPDB.PropertySet propertySet,
    ADB.ObjectId propertySetDefId,
    Dictionary<string, object?> propertySetData,
    ADB.Transaction tr
  )
  {
    var propertySetDef = (AAECPDB.PropertySetDefinition)tr.GetObject(propertySetDefId, ADB.OpenMode.ForRead);

    // build name to ID mapping
    Dictionary<string, int> nameToId = new();
    foreach (AAECPDB.PropertyDefinition propDef in propertySetDef.Definitions)
    {
      nameToId[propDef.Name] = propDef.Id;
    }

    foreach (KeyValuePair<string, object?> kvp in propertySetData)
    {
      string propertyName = kvp.Key;
      object? propertyValueObj = kvp.Value;

      if (
        propertyValueObj is not Dictionary<string, object?> propertyValueDict
        || !propertyValueDict.TryGetValue("value", out object? value)
        || value == null
        || !nameToId.TryGetValue(propertyName, out int propertyId)
      )
      {
        continue;
      }

      try
      {
        SetPropertyValue(propertySet, propertyId, value);
      }
      catch (Exception e) when (!e.IsFatal())
      {
        _logger.LogWarning(e, $"Failed to set property '{propertyName}'");
      }
    }
  }

  private static void SetPropertyValue(AAECPDB.PropertySet propertySet, int propertyId, object value)
  {
    AAECPDB.PropertySetData? propertyData = null;
    foreach (AAECPDB.PropertySetData data in propertySet.PropertySetData)
    {
      if (data.Id == propertyId)
      {
        propertyData = data;
        break;
      }
    }

    if (propertyData == null)
    {
      return;
    }

    // set value based on data type
    switch (propertyData.DataType)
    {
      case AAEC.PropertyData.DataType.Integer:
        propertyData.SetData(Convert.ToInt32(value), propertyData.UnitType);
        break;
      case AAEC.PropertyData.DataType.Real:
        propertyData.SetData(Convert.ToDouble(value), propertyData.UnitType);
        break;
      case AAEC.PropertyData.DataType.TrueFalse:
        propertyData.SetData(Convert.ToBoolean(value), propertyData.UnitType);
        break;
      case AAEC.PropertyData.DataType.Text:
        propertyData.SetData(value.ToString() ?? string.Empty, propertyData.UnitType);
        break;
      case AAEC.PropertyData.DataType.AutoIncrement:
        propertyData.SetData(Convert.ToInt32(value), propertyData.UnitType);
        break;
      case AAEC.PropertyData.DataType.List:
        if (value is List<object> listValue)
        {
          propertyData.SetData(listValue, propertyData.UnitType);
        }
        break;
      // Skip unsupported types (Graphic, AlphaIncrement)
    }
  }

  private static AAEC.PropertyData.DataType GetDataTypeFromValue(object? value)
  {
    return value switch
    {
      int or long => AAEC.PropertyData.DataType.Integer,
      double or float => AAEC.PropertyData.DataType.Real,
      bool => AAEC.PropertyData.DataType.TrueFalse,
      List<object> => AAEC.PropertyData.DataType.List,
      _ => AAEC.PropertyData.DataType.Text
    };
  }
}
