namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the definitions used.
/// </summary>
public class ParameterDefinitionHandler
{
  private sealed record GroupDefinition(string Group, string? Units);

  /// <summary>
  /// Keeps track of all parameter definitions used in the current send operation. This should be attached to the root commit object post conversion.
  /// </summary>
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  private readonly Dictionary<string, GroupDefinition> _groupDefinitions = new();

  public (string internalDefinitionName, string humanReadableName, string groupName, string? units) HandleDefinition(
    DB.Parameter parameter
  )
  {
    var definition = parameter.Definition;

    var internalDefinitionName = definition.Name; // aka real, internal name
    var groupDefinitionId = definition.GetGroupTypeId().TypeId;
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

    if (_groupDefinitions.TryGetValue(groupDefinitionId, out var def))
    {
      return (internalDefinitionName, humanReadableName, def.Group, def.Units);
    }

    string? units = null;
    if (parameter.StorageType == DB.StorageType.Double)
    {
      units = DB.LabelUtils.GetLabelForUnit(parameter.GetUnitTypeId());
    }

    var group = DB.LabelUtils.GetLabelForGroup(definition.GetGroupTypeId());

    _groupDefinitions[groupDefinitionId] = new GroupDefinition(Group: group, Units: units);

    return (internalDefinitionName, humanReadableName, group, units);
  }
}
