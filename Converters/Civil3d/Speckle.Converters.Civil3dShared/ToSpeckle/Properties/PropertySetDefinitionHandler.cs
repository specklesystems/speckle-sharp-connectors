using Speckle.Converters.Civil3dShared.Helpers;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the property set definitions used.
/// </summary>
public class PropertySetDefinitionHandler
{
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

  /// <summary>(set name → field name → FieldBucketId), fed back from PropertySetExtractor while it parses
  /// instance VALUES: the definition API exposes no bucket id, but PropertySetData does. A definition that is
  /// never attached to a sent object simply has no entries — the bundle ships NULL and receive falls back to
  /// field-name matching.</summary>
  public Dictionary<string, Dictionary<string, string>> FieldBucketIds { get; } = new();

  /// <summary>Records one field's FieldBucketId observed on an instance (see <see cref="FieldBucketIds"/>).</summary>
  public void RecordFieldBucketId(string setName, string fieldName, string bucketId)
  {
    if (string.IsNullOrEmpty(bucketId))
    {
      return;
    }
    if (!FieldBucketIds.TryGetValue(setName, out var byField))
    {
      byField = new Dictionary<string, string>();
      FieldBucketIds[setName] = byField;
    }
    byField[fieldName] = bucketId;
  }

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

      // accessing unit type prop can be expected to throw if it's not applicable to the definition
      PropertyHandler propHandler = new();
      propHandler.TryAddToDictionary(propertyDict, "units", () => propertyDefinition.UnitType.GetTypeDisplayName(true));

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
    // Set-level description (member confirmed: PropertySetBaker once wrote propSetDef.Description).
    // applies_to is NOT captured: only AppliesToAll has repo evidence; the expected filter member is
    // AAECPDB.PropertySetDefinition.AppliesToFilter — wire it here once confirmed on the real API.
    PropertyHandler setPropHandler = new();
    if (setPropHandler.TryGetValue(() => setDefinition.Description, out string? setDescription)
        && !string.IsNullOrEmpty(setDescription))
    {
      SetDescriptions[name] = setDescription!;
    }

    return propertyDefinitionNames;
  }
}
