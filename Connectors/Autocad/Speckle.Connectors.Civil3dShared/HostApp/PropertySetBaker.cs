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
  public const string DEFINITIONS_CARRIER_APP_ID = "speckle:civil3d:property-set-definitions";

  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ILogger<PropertySetBaker> _logger;
  private readonly PropertyHandler _propertyHandler;

  /// <summary>Map of property set definition name to every definition baked under that name, in schema order.
  /// A LIST because Civil 3D allows two same-named definitions and the bundle keeps them apart by set_key; the
  /// value paths carry only the name, so which one an object's values belong to is decided per object by
  /// <see cref="SelectDefinition"/> [ENG-9363]. Populated during ParsePropertySetDefinitions / BakeSchemas.</summary>
  private readonly Dictionary<string, List<BakedDefinition>> _propertySetDefinitionMap = new();

  /// <summary>One definition baked this run: its ObjectId, its (FieldBucketId → field name) rebind index, and
  /// its field names. The latter two are what tell same-named definitions apart. Both are empty for
  /// carrier-built definitions (tier 2), which never shipped bucket ids.</summary>
  private sealed record BakedDefinition(
    ADB.ObjectId DefId,
    Dictionary<string, string> BucketToFieldName,
    HashSet<string> FieldNames
  );

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

    // The AEC dictionary API, NOT a raw NOD walk: the defs dictionary is not keyed "AecPropertySetDefs" in the
    // NOD, so the old walk enumerated nothing — the purge was a silent no-op on every drawing, which is what let
    // stale definitions survive into eDuplicateKey / duplicate-set territory [ENG-9328].
    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(db);
    foreach (string? name in propSetDefs.NamesInUse)
    {
      if (name is null || !name.Contains(namePrefix))
      {
        continue;
      }
      try
      {
        var propSetDef = (AAECPDB.PropertySetDefinition)tr.GetObject(propSetDefs.GetAt(name), ADB.OpenMode.ForWrite);
        propSetDef.Erase();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // The bake tolerates a survivor (see AddDefinitionRecord), so this is diagnostic, not fatal [ENG-9328].
        _logger.LogWarning(ex, "Failed to purge property set definition {Name}", name);
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
    ResetRunCounters();

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

      // Per-definition isolation: one bad set (a name collision, an AEC validation error) must not take the whole
      // receive down with a raw "eDuplicateKey" on the model card [ENG-9328].
      try
      {
        ADB.ObjectId defId = CreatePropertySetDefinition(setName, propertyDefinitions, namePrefix, tr);
        if (!defId.IsNull)
        {
          Register(setName, defId, new Dictionary<string, string>(), new HashSet<string>(propertyDefinitions.Keys));
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Failed to create property set definition {SetName}; its values will not bake", setName);
      }
    }

    tr.Commit();
  }

  /// <summary>Tier 1/3 of the definition ladder: recreate defs from host-API-free schemas (the
  /// <c>eav.property_set_definitions</c> file, or synthesis from value rows). The carrier path
  /// (<see cref="ParseAndBakePropertySetDefinitions(Dictionary{string, object?}, string)"/>) stays tier 2.</summary>
  public void BakeSchemas(IReadOnlyList<PropertySetSchema> schemas, string namePrefix)
  {
    _propertySetDefinitionMap.Clear();
    ResetRunCounters();
    if (schemas.Count == 0)
    {
      return;
    }

    using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();
    foreach (var schema in schemas)
    {
      // Two same-named schemas (distinct set_key) are BOTH baked — the second lands under "{name}-{prefix}",
      // because the reuse check below compares set_key and cannot match it to the first. Which one an object's
      // values attach to is decided per object by SelectDefinition [ENG-9363].
      // Naming/collision policy [user feedback: no blanket project-name prefixing]:
      //   (a) an existing def with the ORIGINAL name that is schema-identical (same set_key recipe over its
      //       live fields) is REUSED — no create, no prefix; re-receives of unchanged schemas converge here,
      //       which is also why plain-named defs never need purging;
      //   (b) an existing def that differs gets a disambiguated '{name}-{prefix}' create + warning
      //       (the prefixed name is also what PurgePropertySets can safely reclaim next receive);
      //   (c) no existing def → create under the plain authored name.
      try
      {
        ADB.ObjectId defId;
        if (TryFindExistingDefinition(schema.SetName, tr, out ADB.ObjectId existingId))
        {
          string incomingKey = PropertySetDefinitionLadder.EffectiveSetKey(schema);
          // Stamp first: a def RECREATED by a previous receive hashes differently from the authored one
          // (UnitType is not restorable from the schema, and the recipe includes the unit), so recomputing over
          // the live fields wrongly took the "differs" branch on every re-receive [ENG-9328].
          string existingKey =
            TryReadSetKeyStamp(existingId, tr) ?? ComputeExistingDefinitionKey(schema.SetName, existingId, tr);
          if (string.Equals(incomingKey, existingKey, StringComparison.OrdinalIgnoreCase))
          {
            Register(schema, existingId);
            DefinitionsReused++;
            CleanupRetryCopies(schema.SetName, incomingKey, existingId, tr);
            continue;
          }
          _logger.LogWarning(
            "Existing property set definition {SetName} differs from the received schema; creating {NewName}",
            schema.SetName,
            $"{schema.SetName}-{namePrefix}"
          );
          defId = CreatePropertySetDefinitionFromSchema(schema, $"{schema.SetName}-{namePrefix}", tr);
        }
        else
        {
          defId = CreatePropertySetDefinitionFromSchema(schema, schema.SetName, tr);
        }
        if (defId.IsNull)
        {
          continue;
        }
        Register(schema, defId);
        CleanupRetryCopies(schema.SetName, PropertySetDefinitionLadder.EffectiveSetKey(schema), defId, tr);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // Per-definition isolation — see ParseAndBakePropertySetDefinitions [ENG-9328].
        _logger.LogWarning(
          ex,
          "Failed to create property set definition {SetName}; its values will not bake",
          schema.SetName
        );
      }
    }
    tr.Commit();
  }

  /// <summary>Adds <paramref name="propSetDef"/> to the drawing under <paramref name="recordName"/>. A same-named
  /// record is the previous receive's definition that <see cref="PurgePropertySets"/> could not erase (AEC refuses
  /// while the erased entities' property sets still reference it): erase it here, and if that fails too, reuse it —
  /// the record name is connector-stamped, so it IS the same set. Either way "eDuplicateKey" never escapes to
  /// the model card [ENG-9328].</summary>
  private ADB.ObjectId AddDefinitionRecord(
    AAECPDB.DictionaryPropertySetDefinitions propSetDefs,
    string recordName,
    AAECPDB.PropertySetDefinition propSetDef,
    ADB.Transaction tr
  )
  {
    if (TryFindExistingDefinition(recordName, tr, out ADB.ObjectId existingId))
    {
      try
      {
        var existing = (AAECPDB.PropertySetDefinition)tr.GetObject(existingId, ADB.OpenMode.ForWrite);
        existing.Erase();
        _logger.LogWarning(
          "Erased property set definition {RecordName} that survived the pre-receive purge",
          recordName
        );
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(
          ex,
          "Reusing existing property set definition {RecordName}: it could not be erased",
          recordName
        );
        DefinitionsReused++;
        propSetDef.Dispose();
        return existingId;
      }
    }

    // An erased same-key entry can still occupy the dictionary until commit, so a same-name AddNewRecord may
    // throw eDuplicateKey inside this very transaction — fall back to a unique record name rather than losing
    // the definition (the attach path maps by ObjectId; the record name is only a label) [ENG-9328].
    string name = recordName;
    for (int attempt = 2; ; attempt++)
    {
      try
      {
        propSetDefs.AddNewRecord(name, propSetDef);
        break;
      }
      catch (Exception ex) when (!ex.IsFatal() && attempt <= 4)
      {
        _logger.LogWarning(
          ex,
          "AddNewRecord collided for {RecordName}; retrying as {NewName}",
          name,
          $"{recordName}-r{attempt}"
        );
        name = $"{recordName}-r{attempt}";
      }
    }
    tr.AddNewlyCreatedDBObject(propSetDef, true);
    DefinitionsCreated++;
    return propSetDef.ObjectId;
  }

  /// <summary>Per-run definition outcome counters, reset by the bake entry points — surfaced into the artefact
  /// session log so a broken re-receive is diagnosable offline [ENG-9328].</summary>
  public int DefinitionsCreated { get; private set; }
  public int DefinitionsReused { get; private set; }

  public int DefinitionCount => _propertySetDefinitionMap.Values.Sum(baked => baked.Count);

  private void ResetRunCounters()
  {
    DefinitionsCreated = 0;
    DefinitionsReused = 0;
  }

  private void Register(PropertySetSchema schema, ADB.ObjectId defId)
  {
    var bucketMap = new Dictionary<string, string>();
    var fieldNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var field in schema.Fields)
    {
      fieldNames.Add(field.Name);
      if (field.BucketId is not null)
      {
        bucketMap[field.BucketId] = field.Name;
      }
    }
    Register(schema.SetName, defId, bucketMap, fieldNames);
  }

  private void Register(
    string setName,
    ADB.ObjectId defId,
    Dictionary<string, string> bucketMap,
    HashSet<string> fieldNames
  )
  {
    if (!_propertySetDefinitionMap.TryGetValue(setName, out var baked))
    {
      baked = new List<BakedDefinition>();
      _propertySetDefinitionMap[setName] = baked;
    }
    if (baked.Count > 0)
    {
      _logger.LogInformation(
        "Property set name {SetName} now has {Count} definitions; values are matched to one by field_bucket_id",
        setName,
        baked.Count + 1
      );
    }
    baked.Add(new BakedDefinition(defId, bucketMap, fieldNames));
  }

  /// <summary>Picks which of several same-named definitions THIS object's values belong to. The value paths
  /// carry only the set name, so the discriminator is field_bucket_id membership — the rule the spec names for
  /// exactly this case — with field-name overlap as the tiebreak for producers that ship no bucket ids. The
  /// single-candidate case (every ordinary drawing) short-circuits and behaves as before [ENG-9363].</summary>
  private static BakedDefinition SelectDefinition(List<BakedDefinition> candidates, Dictionary<string, object?> setData)
  {
    if (candidates.Count == 1)
    {
      return candidates[0];
    }

    BakedDefinition best = candidates[0];
    int bestScore = -1;
    foreach (var candidate in candidates)
    {
      int score = 0;
      foreach (var entry in setData)
      {
        string? bucketId =
          entry.Value is Dictionary<string, object?> leaf && leaf.TryGetValue("internalDefinitionName", out var idn)
            ? idn as string
            : null;
        // A bucket-id hit is hard evidence; a field-name hit is weaker (same-named sets often share names).
        if (bucketId is { Length: > 0 } && candidate.BucketToFieldName.ContainsKey(bucketId))
        {
          score += 2;
        }
        else if (candidate.FieldNames.Contains(entry.Key))
        {
          score += 1;
        }
      }
      if (score > bestScore)
      {
        bestScore = score;
        best = candidate;
      }
    }
    return best;
  }

  private const string SET_KEY_XRECORD = "SPECKLE_SET_KEY";

  /// <summary>Stamps the authoritative set_key on a definition (xrecord in its extension dictionary), so a later
  /// receive can compare against the AUTHORED identity instead of rehashing lossy recreated fields [ENG-9328].</summary>
  private void StampSetKey(ADB.ObjectId defId, string setKey, ADB.Transaction tr)
  {
    try
    {
      var def = (AAECPDB.PropertySetDefinition)tr.GetObject(defId, ADB.OpenMode.ForWrite);
      if (def.ExtensionDictionary.IsNull)
      {
        def.CreateExtensionDictionary();
      }
      var ext = (ADB.DBDictionary)tr.GetObject(def.ExtensionDictionary, ADB.OpenMode.ForWrite);
      using var xrec = new ADB.Xrecord
      {
        Data = new ADB.ResultBuffer(new ADB.TypedValue((int)ADB.DxfCode.Text, setKey)),
      };
      if (ext.Contains(SET_KEY_XRECORD))
      {
        ext.Remove(SET_KEY_XRECORD);
      }
      ext.SetAt(SET_KEY_XRECORD, xrec);
      tr.AddNewlyCreatedDBObject(xrec, true);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Could not stamp set_key on property set definition");
    }
  }

  private string? TryReadSetKeyStamp(ADB.ObjectId defId, ADB.Transaction tr)
  {
    try
    {
      var def = (AAECPDB.PropertySetDefinition)tr.GetObject(defId, ADB.OpenMode.ForRead);
      if (def.ExtensionDictionary.IsNull)
      {
        return null;
      }
      var ext = (ADB.DBDictionary)tr.GetObject(def.ExtensionDictionary, ADB.OpenMode.ForRead);
      if (!ext.Contains(SET_KEY_XRECORD))
      {
        return null;
      }
      var xrec = (ADB.Xrecord)tr.GetObject(ext.GetAt(SET_KEY_XRECORD), ADB.OpenMode.ForRead);
      using ADB.ResultBuffer? data = xrec.Data;
      if (data is not null)
      {
        foreach (ADB.TypedValue tv in data)
        {
          if (tv.Value is string s && s.Length > 0)
          {
            return s;
          }
        }
      }
      return null;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }

  /// <summary>Exact-name lookup via the AEC dictionary API. The previous raw NOD walk keyed on a dictionary name
  /// that does not exist, so this NEVER found anything — every receive then "created" the same plain name, and the
  /// collision retry minted a fresh -rN copy per receive [ENG-9328].</summary>
  private bool TryFindExistingDefinition(string setName, ADB.Transaction tr, out ADB.ObjectId defId)
  {
    defId = ADB.ObjectId.Null;
    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(_settingsStore.Current.Document.Database);
    if (!propSetDefs.Has(setName, tr))
    {
      return false;
    }
    defId = propSetDefs.GetAt(setName);
    return !defId.IsNull;
  }

  /// <summary>Erases the collision-retry copies ({setName}-rN) earlier builds minted when the broken lookup made
  /// every receive re-create the same set. Only records stamped with the SAME set_key are touched — a user-authored
  /// def that merely looks like a retry name carries no stamp and survives.</summary>
  private void CleanupRetryCopies(string setName, string setKey, ADB.ObjectId keepId, ADB.Transaction tr)
  {
    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(_settingsStore.Current.Document.Database);
    for (int i = 2; i <= 9; i++)
    {
      string name = $"{setName}-r{i}";
      if (!propSetDefs.Has(name, tr))
      {
        continue;
      }
      ADB.ObjectId id = propSetDefs.GetAt(name);
      if (id == keepId || !string.Equals(TryReadSetKeyStamp(id, tr), setKey, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      try
      {
        ((AAECPDB.PropertySetDefinition)tr.GetObject(id, ADB.OpenMode.ForWrite)).Erase();
        _logger.LogWarning("Erased stale retry copy {Name} of property set {SetName}", name, setName);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Could not erase stale retry copy {Name}", name);
      }
    }
  }

  /// <summary>The set_key recipe computed over a LIVE definition's fields, for reuse-if-identical. Mirrors
  /// the send-side capture exactly: DataType.ToString(), unit = PropertyHandler.TryGetUnitDisplay (throw and
  /// "(none)" both → null). Must stay in step with PropertySetDefinitionHandler: filter here without filtering
  /// there (or vice versa) and every re-receive of an unchanged schema mints a "-{prefix}" copy [ENG-9360].</summary>
  private string ComputeExistingDefinitionKey(string setName, ADB.ObjectId defId, ADB.Transaction tr)
  {
    var setDefinition = (AAECPDB.PropertySetDefinition)tr.GetObject(defId, ADB.OpenMode.ForRead);
    var fields = new List<(string Name, string? DataType, string? Unit)>();
    foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
    {
      string? unit = _propertyHandler.TryGetUnitDisplay(() => propDef.UnitType.GetTypeDisplayName(true));
      fields.Add((propDef.Name, propDef.DataType.ToString(), unit));
    }
    return PropertySetDefinitionLadder.ComputeSetKey(setName, fields);
  }

  /// <summary>Scopes the definition to the entity types the sender captured in applies_to; a NULL/empty column
  /// means apply-to-all — the sender either had an apply-to-all set, or is a producer that cannot enumerate the
  /// filter (dwgextract) [ENG-9362]. Object-based only: the sender never publishes style filters, so byStyle is
  /// false. A class name the receiving Civil 3D release does not know makes SetAppliesToFilter throw — that
  /// degrades to apply-to-all rather than losing the definition.</summary>
  private void ApplyAppliesTo(AAECPDB.PropertySetDefinition propSetDef, PropertySetSchema schema)
  {
    string[] classNames = schema.AppliesTo?.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray() ?? [];
    if (classNames.Length == 0)
    {
      propSetDef.AppliesToAll = true;
      return;
    }

    try
    {
      System.Collections.Specialized.StringCollection filter = new();
      filter.AddRange(classNames);
      propSetDef.SetAppliesToFilter(filter, false);
      propSetDef.AppliesToAll = false;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(
        ex,
        "Could not scope property set definition {SetName} to {AppliesTo}; it applies to all object types",
        schema.SetName,
        schema.AppliesTo
      );
      propSetDef.AppliesToAll = true;
    }
  }

  private ADB.ObjectId CreatePropertySetDefinitionFromSchema(
    PropertySetSchema schema,
    string recordName,
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
    ApplyAppliesTo(propSetDef, schema);

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
      object? defaultValue =
        field.DefaultBoolean.HasValue ? field.DefaultBoolean.Value
        : field.DefaultDouble.HasValue ? field.DefaultDouble.Value
        : field.DefaultString;
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

    ADB.ObjectId defId = AddDefinitionRecord(propSetDefs, recordName, propSetDef, tr);
    if (!defId.IsNull)
    {
      // Stamp created AND reused defs with the authored set_key — the identity every later receive compares
      // against (recomputing from the recreated fields is lossy; see the stamp read in BakeSchemas).
      StampSetKey(defId, PropertySetDefinitionLadder.EffectiveSetKey(schema), tr);
    }
    return defId;
  }

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
      if (!_propertySetDefinitionMap.TryGetValue(setName, out var candidates) || candidates.Count == 0)
      {
        _logger.LogWarning("Property set definition {SetName} not found in definition map", setName);
        return false;
      }

      BakedDefinition baked = SelectDefinition(candidates, setData);
      if (baked.DefId.IsNull)
      {
        return false;
      }

      return AddPropertySetToEntity(entity, setName, baked, setData, tr);
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
    // The tier-2 carrier never shipped an entity-type filter, so apply-to-all is all this path can honour;
    // tier-1 schemas go through ApplyAppliesTo instead [ENG-9362].
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

    return AddDefinitionRecord(propSetDefs, prefixedName, propSetDef, tr);
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
    BakedDefinition baked,
    Dictionary<string, object?> setData,
    ADB.Transaction tr
  )
  {
    ADB.ObjectId propertySetDefId = baked.DefId;
    try
    {
      if (!entity.IsWriteEnabled)
      {
        entity.UpgradeOpen();
      }

      // Idempotent attach: a set already on the entity (whatever put it there) gets its VALUES written instead of
      // aborting — the previous already-exists throw silently left re-received sets empty [ENG-9328].
      if (!ObjectHasPropertySet(entity, propertySetDefId))
      {
        AAECPDB.PropertyDataServices.AddPropertySet(entity, propertySetDefId);
      }

      return TrySetPropertyValues(entity, baked, setData, tr);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to add property set {SetName} to entity", setName);
      return false;
    }
  }

  private bool TrySetPropertyValues(
    ADB.Entity entity,
    BakedDefinition baked,
    Dictionary<string, object?> setData,
    ADB.Transaction tr
  )
  {
    ADB.ObjectId propertySetDefId = baked.DefId;
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

      var bucketMap = baked.BucketToFieldName;
      foreach (var propertyEntry in setData)
      {
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
