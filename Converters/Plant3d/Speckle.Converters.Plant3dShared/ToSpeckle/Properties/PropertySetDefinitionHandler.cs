using Speckle.Converters.Plant3dShared.Helpers;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the property set definitions used.
/// </summary>
public class PropertySetDefinitionHandler
{
  /// <summary>
  /// Keeps track of all property set definitions used in the current send operation.
  /// This should be added to the properties dict on the root commit object post conversion.
  /// </summary>
  public Dictionary<string, Dictionary<string, object?>> Definitions { get; } = new();

  // Keys used for the dictionary representing a single property set definition
  public const string PROP_SET_DEF_NAME_KEY = "name";
  public const string PROP_SET_PROP_DEFS_KEY = "propertyDefinitions";

  // Keys used for individual property definitions within a single property set definition
  public const string PROP_DEF_NAME_KEY = "name";
  public const string PROP_DEF_DESCRIPTION_KEY = "description";
  public const string PROP_DEF_ID_KEY = "id";
  public const string PROP_DEF_TYPE_KEY = "dataType";
  public const string PROP_DEF_DEFAULT_VALUE_KEY = "defaultValue";

  /// <summary>
  /// Extracts out and stores in <see cref="Definitions"/> the property set definition.
  /// </summary>
  /// <param name="setDefinition">The property set definition. Assumes this is opened for Read already.</param>
  public Dictionary<int, string> HandleDefinition(AAECPDB.PropertySetDefinition setDefinition)
  {
    Dictionary<string, object?> propertyDefinitionsDict = new();
    Dictionary<int, string> propertyDefinitionNames = new();
    foreach (AAECPDB.PropertyDefinition propertyDefinition in setDefinition.Definitions)
    {
      string propertyName = propertyDefinition.Name;
      propertyDefinitionNames[propertyDefinition.Id] = propertyName;
      var propertyDict = new Dictionary<string, object?>()
      {
        [PROP_DEF_NAME_KEY] = propertyName,
        [PROP_DEF_DESCRIPTION_KEY] = propertyDefinition.Description,
        [PROP_DEF_ID_KEY] = propertyDefinition.Id,
        [PROP_DEF_TYPE_KEY] = propertyDefinition.DataType.ToString(),
        [PROP_DEF_DEFAULT_VALUE_KEY] = propertyDefinition.DefaultData
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
      [PROP_SET_PROP_DEFS_KEY] = propertyDefinitionsDict
    };

    return propertyDefinitionNames;
  }
}
