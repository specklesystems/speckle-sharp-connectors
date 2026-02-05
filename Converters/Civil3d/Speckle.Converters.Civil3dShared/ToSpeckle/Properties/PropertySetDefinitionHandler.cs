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

    return propertyDefinitionNames;
  }
}
