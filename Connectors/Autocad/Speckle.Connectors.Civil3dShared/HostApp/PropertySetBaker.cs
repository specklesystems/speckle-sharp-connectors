using System.Globalization;
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

  public const string DEFINITIONS_CARRIER_APP_ID = "speckle:civil3d:property-set-definitions";

  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetBaker> _logger;
  private readonly PropertyHandler _propertyHandler;

  /// <summary>
  /// Map of property set definition name to its ObjectId. Populated during ParsePropertySetDefinitions.
  /// </summary>
  private readonly Dictionary<string, ADB.ObjectId> _propertySetDefinitionMap = new();

#if SDK_BUNDLE_VOCAB_ADDITIONS
  /// <summary>set name → (FieldBucketId → field name), from tier-1/3 schemas — the rebind's join index.
  /// Empty for carrier-built definitions (tier 2), which never shipped bucket ids.</summary>
  private readonly Dictionary<string, Dictionary<string, string>> _bucketToFieldNameBySet = new();
#endif

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
    if (rootObject[ProxyKeys.PROPERTYSET_DEFINITIONS] is not Dictionary<string, object?> definitions)
    {
      _propertySetDefinitionMap.Clear();
      return;
    }

    ParseAndBakePropertySetDefinitions(definitions, namePrefix);
  }

  public void ParseAndBakePropertySetDefinitions(Dictionary<string, object?> definitions, string namePrefix)
  {
    _propertySetDefinitionMap.Clear();
#if SDK_BUNDLE_VOCAB_ADDITIONS
    _bucketToFieldNameBySet.Clear();
#endif

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

#if SDK_BUNDLE_VOCAB_ADDITIONS
  /// <summary>Tier 1/3 of the definition ladder: recreate defs from host-API-free schemas (the
  /// <c>eav.property_set_definitions</c> file, or synthesis from value rows). The carrier path
  /// (<see cref="ParseAndBakePropertySetDefinitions(Dictionary{string, object?}, string)"/>) stays tier 2.</summary>
  public void BakeSchemas(IReadOnlyList<PropertySetSchema> schemas, string namePrefix)
  {
    _propertySetDefinitionMap.Clear();
    _bucketToFieldNameBySet.Clear();
    if (schemas.Count == 0)
    {
      return;
    }

    using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();
    foreach (var schema in schemas)
    {
      if (_propertySetDefinitionMap.ContainsKey(schema.SetName))
      {
        // Two same-named sets (distinct set_key). The name-keyed map — and the value paths, which only
        // carry the name — cannot address both: first wins, matching send's first-wins Definitions dict.
        _logger.LogWarning("Duplicate property set name {SetName}; keeping the first definition", schema.SetName);
        continue;
      }
      ADB.ObjectId defId = CreatePropertySetDefinitionFromSchema(schema, namePrefix, tr);
      if (defId.IsNull)
      {
        continue;
      }
      _propertySetDefinitionMap[schema.SetName] = defId;
      var bucketMap = new Dictionary<string, string>();
      foreach (var field in schema.Fields)
      {
        if (field.BucketId is not null)
        {
          bucketMap[field.BucketId] = field.Name;
        }
      }
      if (bucketMap.Count > 0)
      {
        _bucketToFieldNameBySet[schema.SetName] = bucketMap;
      }
    }
    tr.Commit();
  }

  private ADB.ObjectId CreatePropertySetDefinitionFromSchema(
    PropertySetSchema schema,
    string namePrefix,
    ADB.Transaction tr
  )
  {
    var db = _settingsStore.Current.Document.Database;
    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(db);

    AAECPDB.PropertySetDefinition propSetDef = new();
    propSetDef.SetToStandard(db);
    propSetDef.SubSetDatabaseDefaults(db);
    if (!string.IsNullOrEmpty(schema.SetDescription))
    {
      propSetDef.Description = schema.SetDescription;
    }
    // schema.AppliesTo is shipped but not applied: only AppliesToAll has repo-confirmed API surface
    // (expected filter member: PropertySetDefinition.AppliesToFilter — wire when confirmed on Windows).
    propSetDef.AppliesToAll = true;

    foreach (var field in schema.Fields)
    {
      // Tier-3 schemas always carry a type; a null (malformed file row) degrades to Text rather than
      // failing the whole set — the value coercion path re-checks per value anyway.
      if (!Enum.TryParse(field.DataType, out AAEC.PropertyData.DataType dataType))
      {
        dataType = AAEC.PropertyData.DataType.Text;
      }
      AAECPDB.PropertyDefinition propDef = new() { DataType = dataType, Name = field.Name };
      propDef.SetToStandard(db);
      propDef.SubSetDatabaseDefaults(db);
      if (!string.IsNullOrEmpty(field.Description))
      {
        propDef.Description = field.Description;
      }
      object? defaultValue = field.DefaultBoolean ?? (object?)field.DefaultDouble ?? field.DefaultString;
      if (defaultValue is not null)
      {
        try
        {
          propDef.DefaultData = dataType switch
          {
            AAEC.PropertyData.DataType.Integer => Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture),
            AAEC.PropertyData.DataType.AutoIncrement => Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture),
            _ => defaultValue,
          };
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogWarning(ex, "Failed to set default for property {PropertyName}", field.Name);
        }
      }
      propSetDef.Definitions.Add(propDef);
    }

    propSetDefs.AddNewRecord($"{schema.SetName}-{namePrefix}", propSetDef);
    tr.AddNewlyCreatedDBObject(propSetDef, true);
    return propSetDef.ObjectId;
  }
#endif

  /// <summary>
  /// Try to bake property sets from a Speckle object to a Civil3D entity.
  /// </summary>
  public bool TryBakePropertySets(ADB.Entity entity, Base sourceObject, ADB.Transaction tr)
  {
    if (sourceObject["properties"] is not Dictionary<string, object?> properties)
    {
      return false;
    }

    return TryBakePropertySets(entity, properties, tr);
  }

  public bool TryBakePropertySets(ADB.Entity entity, Dictionary<string, object?> properties, ADB.Transaction tr)
  {
    if (
      !properties.TryGetValue("Property Sets", out var propertySetsObj)
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

      return AddPropertySetToEntity(entity, setName, propertySetDefId, setData, tr);
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
            AAEC.PropertyData.DataType.Integer => Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture),
            AAEC.PropertyData.DataType.AutoIncrement => Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture),
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
    string setName,
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

      return TrySetPropertyValues(entity, setName, propertySetDefId, setData, tr);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to add property set to entity");
      return false;
    }
  }

  private bool TrySetPropertyValues(
    ADB.Entity entity,
    string setName,
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

      // Build a map of property names to definition IDs + data types (for value coercion below)
      Dictionary<string, (int Id, AAEC.PropertyData.DataType Type)> propertyNameToDef = new();
      foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
      {
        propertyNameToDef[propDef.Name] = (propDef.Id, propDef.DataType);
      }

#if SDK_BUNDLE_VOCAB_ADDITIONS
      _bucketToFieldNameBySet.TryGetValue(setName, out var bucketMap);
#else
      _ = setName;
#endif
      foreach (var propertyEntry in setData)
      {
#if SDK_BUNDLE_VOCAB_ADDITIONS
        // Bucket-id-first field matching: internalDefinitionName is the FieldBucketId the producer shipped;
        // the display key is only the fallback (it can drift from the authored field name).
        string propertyName = PropertySetDefinitionLadder.ResolveFieldName(
          propertyEntry.Key,
          propertyEntry.Value is Dictionary<string, object?> idnDict
          && idnDict.TryGetValue("internalDefinitionName", out var idnObj)
            ? idnObj as string
            : null,
          bucketMap
        );
#else
        string propertyName = propertyEntry.Key;
#endif

        object? value = propertyEntry.Value is Dictionary<string, object?> propertyDataDict
          ? propertyDataDict.TryGetValue("value", out var nested)
            ? nested
            : null
          : propertyEntry.Value;

        if (value == null)
        {
          continue;
        }

        if (!propertyNameToDef.TryGetValue(propertyName, out var propDefInfo))
        {
          continue;
        }

        // The eav path round-trips every number as double, and SetAt's failure is swallowed by the handler —
        // without coercion an Integer-typed property silently stays unset (empty in the palette). Mirror the
        // default-value cast in CreatePropertySetDefinition, driven by the definition's data type.
        object coercedValue;
        try
        {
          coercedValue = propDefInfo.Type switch
          {
            AAEC.PropertyData.DataType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            AAEC.PropertyData.DataType.AutoIncrement => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            AAEC.PropertyData.DataType.Real => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            AAEC.PropertyData.DataType.Text => value.ToString() ?? "",
            AAEC.PropertyData.DataType.TrueFalse => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            _ => value,
          };
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogWarning(
            ex,
            "Could not coerce received value for property {PropertyName} to {DataType}",
            propertyName,
            propDefInfo.Type
          );
          continue;
        }

        _propertyHandler.TryGetValue(
          () =>
          {
            propertySet.SetAt(propDefInfo.Id, coercedValue);
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
