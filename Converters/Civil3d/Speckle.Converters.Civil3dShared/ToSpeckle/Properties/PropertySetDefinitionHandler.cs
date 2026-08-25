using Microsoft.Extensions.Logging;
using Speckle.Converters.Civil3dShared.Helpers;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the property set definitions used.
/// </summary>
public class PropertySetDefinitionHandler
{
  private readonly ILogger<PropertySetDefinitionHandler> _logger;

  public PropertySetDefinitionHandler(ILogger<PropertySetDefinitionHandler> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Keeps track of all property set definitions used in the current send operation. This should be added to the properties dict on the root commit object post conversion.
  /// </summary>
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  /// POC: We're storing these by property set def name atm. There is a decent change different property sets can have the same name, need to validate this.
  public Dictionary<string, Dictionary<string, object?>> Definitions { get; } = new();

  /// <summary>Set-level authored description per set name (PropertySetDefinition.Description); absent = none.
  /// Kept OUT of <see cref="Definitions"/> so the carrier-object shape stays byte-compatible.</summary>
  public Dictionary<string, string> SetDescriptions { get; } = new();

  /// <summary>Authored field order per set name. Dictionary enumeration order is not contractual, and the
  /// bundle's property_set_definitions file promises ROW ORDER = FIELD ORDER — this list is that promise.</summary>
  public Dictionary<string, List<string>> FieldOrders { get; } = new();

  /// <summary>(set name → field name → FieldBucketId) read straight off the DEFINITION
  /// (PropertyDefinition.FieldBucketId). This is the authoritative source: it covers every authored field,
  /// including ones no sent entity carried a value row for, so the bundle's field_bucket_id — the rebind join
  /// key — is no longer NULL for them [ENG-9361]. Kept OUT of <see cref="Definitions"/> alongside
  /// <see cref="SetDescriptions"/> so the carrier-object shape stays byte-compatible.</summary>
  public Dictionary<string, Dictionary<string, string>> DefinitionFieldBucketIds { get; } = new();

  /// <summary>(set name → field name → FieldBucketId) as OBSERVED on instance values by PropertySetExtractor.
  /// Now only a fallback behind <see cref="DefinitionFieldBucketIds"/>, kept for the case where the definition
  /// getter throws, and as the cross-check that warns when the two disagree.</summary>
  public Dictionary<string, Dictionary<string, string>> FieldBucketIds { get; } = new();

  /// <summary>Records one field's FieldBucketId observed on an instance (see <see cref="FieldBucketIds"/>), and
  /// warns when it contradicts what the definition reported — the two are documented identically ("the field
  /// code Id"), and a mismatch would mean the rebind join key is being read off the wrong member.</summary>
  public void RecordFieldBucketId(string setName, string fieldName, string bucketId)
  {
    if (string.IsNullOrEmpty(bucketId))
    {
      return;
    }
    if (
      DefinitionFieldBucketIds.TryGetValue(setName, out var fromDefinition)
      && fromDefinition.TryGetValue(fieldName, out string? definitionBucketId)
      && !string.Equals(definitionBucketId, bucketId, StringComparison.Ordinal)
      && _warnedBucketIdMismatch.Add((setName, fieldName))
    )
    {
      _logger.LogWarning(
        "FieldBucketId mismatch on {SetName}.{FieldName}: definition reports {DefinitionBucketId}, instance value reports {InstanceBucketId}; publishing the definition's",
        setName,
        fieldName,
        definitionBucketId,
        bucketId
      );
    }
    if (!FieldBucketIds.TryGetValue(setName, out var byField))
    {
      byField = new Dictionary<string, string>();
      FieldBucketIds[setName] = byField;
    }
    byField[fieldName] = bucketId;
  }

  /// <summary>One mismatch warning per (set, field) — these are per-instance callbacks, so an unguarded warning
  /// would fire once per entity.</summary>
  private readonly HashSet<(string SetName, string FieldName)> _warnedBucketIdMismatch = new();

  // Keys used for the dictionary representing a single property set definition
  public const string PROP_SET_DEF_NAME_KEY = "name"; // name of the property set definition
  public const string PROP_SET_PROP_DEFS_KEY = "propertyDefinitions"; // property definitions in this property set definition

  // Keys used for inidividual property definitions within a single property set definition
  public const string PROP_DEF_NAME_KEY = "name";
  public const string PROP_DEF_DESCRIPTION_KEY = "description";
  public const string PROP_DEF_ID_KEY = "id";
  public const string PROP_DEF_TYPE_KEY = "dataType";
  public const string PROP_DEF_DEFAULT_VALUE_KEY = "defaultValue";

  /// <summary>
  /// Extracts out and stores in <see cref="Definitions"/> the property set definition.
  /// </summary>
  /// <param name="setDefinition">The property set definition. Assumes this is opened for Read already.</param>
  /// <returns></returns>
  public Dictionary<int, string> HandleDefinition(AAECPDB.PropertySetDefinition setDefinition)
  {
    Dictionary<string, object?> propertyDefinitionsDict = new(); // this is used to store on the property set definition
    Dictionary<int, string> propertyDefinitionNames = new(); // this is used to pass to the instance for property value retrieval
    List<string> fieldOrder = new(); // authored order — the bundle file's row order contract
    Dictionary<string, string> fieldBucketIdsByField = new();
    PropertyHandler propHandlerForField = new();
    foreach (AAECPDB.PropertyDefinition propertyDefinition in setDefinition.Definitions)
    {
      string propertyName = propertyDefinition.Name;
      propertyDefinitionNames[propertyDefinition.Id] = propertyName;
      fieldOrder.Add(propertyName);
      var propertyDict = new Dictionary<string, object?>()
      {
        [PROP_DEF_NAME_KEY] = propertyName,
        [PROP_DEF_DESCRIPTION_KEY] = propertyDefinition.Description,
        [PROP_DEF_ID_KEY] = propertyDefinition.Id,
        [PROP_DEF_TYPE_KEY] = propertyDefinition.DataType.ToString(),
        [PROP_DEF_DEFAULT_VALUE_KEY] = propertyDefinition.DefaultData,
      };

      // FieldBucketId ("the field code Id") is the eav.internal_definition_name the value rows ship, so this is
      // the rebind join key — read it off the definition rather than waiting to observe it on an instance
      // [ENG-9361]. Guarded like UnitType: these getters can throw for definitions they don't apply to.
      propHandlerForField.TryGetValue(() => propertyDefinition.FieldBucketId, out string? fieldBucketId);
      if (fieldBucketId is { Length: > 0 })
      {
        fieldBucketIdsByField[propertyName] = fieldBucketId;
      }
      propHandlerForField.TryGetValue(() => propertyDefinition.GlobalName, out string? globalName);
      // dwgextract ships GetIndex() (== managed Id) as its bucket id and has an open question whether that
      // matches Autodesk's FieldBucketId; this line is the evidence a single test publish needs [ENG-9361].
      _logger.LogDebug(
        "Property set field {SetName}.{FieldName}: FieldBucketId={FieldBucketId} Id={FieldId} GlobalName={GlobalName}",
        setDefinition.Name,
        propertyName,
        fieldBucketId,
        propertyDefinition.Id,
        globalName
      );

      // Accessing unit type can throw when it's not applicable to the definition, and a unitless definition
      // reports Autodesk's "(none)" placeholder — TryGetUnitDisplay collapses both to null so the definition
      // rows, the value rows and set_key all agree on real-unit-or-absent [ENG-9360].
      if (propHandlerForField.TryGetUnitDisplay(() => propertyDefinition.UnitType.GetTypeDisplayName(true)) is { } unit)
      {
        propertyDict["units"] = unit;
      }

      propertyDefinitionsDict[propertyName] = propertyDict;
    }

    var name = setDefinition.Name;

    if (Definitions.ContainsKey(name))
    {
      return propertyDefinitionNames;
    }

    Definitions[name] = new Dictionary<string, object?>()
    {
      [PROP_SET_DEF_NAME_KEY] = name,
      [PROP_SET_PROP_DEFS_KEY] = propertyDefinitionsDict,
    };
    FieldOrders[name] = fieldOrder;
    DefinitionFieldBucketIds[name] = fieldBucketIdsByField;
    // Set-level description (member confirmed: PropertySetBaker once wrote propSetDef.Description).
    // applies_to is NOT captured: only AppliesToAll has repo evidence; the expected filter member is
    // AAECPDB.PropertySetDefinition.AppliesToFilter — wire it here once confirmed on the real API.
    PropertyHandler setPropHandler = new();
    if (
      setPropHandler.TryGetValue(() => setDefinition.Description, out string? setDescription)
      && !string.IsNullOrEmpty(setDescription)
    )
    {
      SetDescriptions[name] = setDescription!;
    }

    return propertyDefinitionNames;
  }
}
