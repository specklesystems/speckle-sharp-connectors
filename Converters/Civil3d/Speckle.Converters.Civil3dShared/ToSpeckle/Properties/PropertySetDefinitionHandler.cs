using Speckle.Sdk;

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
        ["name"] = propertyName,
        ["description"] = propertyDefinition.Description,
        ["id"] = propertyDefinition.Id,
        ["isReadOnly"] = propertyDefinition.IsReadOnly,
        ["dataType"] = propertyDefinition.DataType.ToString(),
        ["defaultValue"] = propertyDefinition.DefaultData
      };

      try
      {
        // accessing unit type prop can be expected to throw if it's not applicable to the definition
        propertyDict["units"] = propertyDefinition.UnitType.GetTypeDisplayName(true);
      }
      catch (Exception e) when (!e.IsFatal()) { }

      propertyDefinitionsDict[propertyName] = propertyDict;
    }

    var name = setDefinition.Name;

    if (Definitions.ContainsKey(name))
    {
      return propertyDefinitionNames;
    }

    Definitions[name] = new Dictionary<string, object?>()
    {
      ["name"] = name,
      ["propertyDefinitions"] = propertyDefinitionsDict
    };

    return propertyDefinitionNames;
  }
}
