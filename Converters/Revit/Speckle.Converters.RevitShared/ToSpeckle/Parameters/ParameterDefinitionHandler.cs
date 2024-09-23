namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the definitions used.
/// </summary>
public class ParameterDefinitionHandler
{
  /// <summary>
  /// Keeps track of all parameter definitions used in the current send operation. This should be attached to the root commit object post conversion.
  /// </summary>
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  public Dictionary<string, Dictionary<string, object?>> Definitions { get; } = new();

  /// <summary>
  /// Extracts out and stores in <see cref="Definitions"/> the parameter's definition.
  /// </summary>
  /// <param name="parameter"></param>
  /// <returns></returns>
  public (string internalDefinitionName, string humanReadableName, string groupName) HandleDefinition(
    DB.Parameter parameter
  )
  {
    var definition = parameter.Definition;
    var internalDefinitionName = definition.Name; // aka real, internal name
    var humanReadableName = definition.Name;

    if (parameter.IsShared)
    {
      internalDefinitionName = parameter.GUID.ToString(); // Note: unsure it's needed
    }

    if (
      definition is DB.InternalDefinition internalDefinition
      && internalDefinition.BuiltInParameter != DB.BuiltInParameter.INVALID
    )
    {
      internalDefinitionName = internalDefinition.BuiltInParameter.ToString();
    }

#pragma warning disable CA1854 // swapping leads to nullability errors; should be resolved once we type this more strongly.
    if (Definitions.ContainsKey(internalDefinitionName))
#pragma warning restore CA1854
    {
      var def = Definitions[internalDefinitionName];
      return (internalDefinitionName, humanReadableName, def["group"]! as string ?? "unknown group");
    }

    string? units = null;
    if (parameter.StorageType == DB.StorageType.Double)
    {
      units = DB.LabelUtils.GetLabelForUnit(parameter.GetUnitTypeId());
    }

    var group = DB.LabelUtils.GetLabelForGroup(parameter.Definition.GetGroupTypeId());

    Definitions[internalDefinitionName] = new Dictionary<string, object?>()
    {
      ["definitionName"] = internalDefinitionName,
      ["name"] = humanReadableName,
      ["units"] = units,
      ["isShared"] = parameter.IsShared,
      ["isReadOnly"] = parameter.IsReadOnly,
      ["group"] = group
    };

    return (internalDefinitionName, humanReadableName, group);
  }
}
