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
  public (string internalDefinitionName, string humanReadableName, string groupName, string? units) HandleDefinition(
    DB.Parameter parameter
  )
  {
    var definition = parameter.Definition;
    var internalDefinitionName = definition.Name; // aka real, internal name
    var humanReadableName = internalDefinitionName;
    var isShared = parameter.IsShared;

    if (isShared)
    {
      internalDefinitionName = parameter.GUID.ToString(); // Note: unsure it's needed
    }

    if (definition is DB.InternalDefinition internalDefinition)
    {
      var builtInParameter = internalDefinition.BuiltInParameter;
      if (builtInParameter != DB.BuiltInParameter.INVALID)
      {
        internalDefinitionName = builtInParameter.ToString();
      }
    }

    if (Definitions.TryGetValue(internalDefinitionName, out var def))
    {
      return (
        internalDefinitionName,
        humanReadableName,
        def["group"] as string ?? "unknown group",
        def["units"] as string
      );
    }

    string? units = null;
    if (parameter.StorageType == DB.StorageType.Double)
    {
      units = DB.LabelUtils.GetLabelForUnit(parameter.GetUnitTypeId());
    }

    var group = DB.LabelUtils.GetLabelForGroup(definition.GetGroupTypeId());

    Definitions[internalDefinitionName] = new Dictionary<string, object?>()
    {
      ["definitionName"] = internalDefinitionName,
      ["name"] = humanReadableName,
      ["units"] = units,
      ["isShared"] = isShared,
      ["isReadOnly"] = parameter.IsReadOnly,
      ["group"] = group
    };

    return (internalDefinitionName, humanReadableName, group, units);
  }
}
