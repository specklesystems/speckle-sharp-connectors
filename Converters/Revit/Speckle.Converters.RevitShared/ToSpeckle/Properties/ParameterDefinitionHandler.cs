namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Keeps track during a send conversion operation of the definitions used.
/// </summary>
public class ParameterDefinitionHandler
{
  private sealed record ParameterDefinition(string GroupName, string? Units);

  private sealed record ParameterKey(string InternalName, string Group);

  /// <summary>
  /// Keeps track of all parameter definitions used in the current send operation. This should be attached to the root commit object post conversion.
  /// </summary>
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  private readonly Dictionary<ParameterKey, ParameterDefinition> _parameterDefinitions = new();

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
    var key = new ParameterKey(internalDefinitionName, groupDefinitionId);
    if (_parameterDefinitions.TryGetValue(key, out var parameterDefinition))
    {
      return (internalDefinitionName, humanReadableName, parameterDefinition.GroupName, parameterDefinition.Units);
    }
    var group = DB.LabelUtils.GetLabelForGroup(definition.GetGroupTypeId());

    string? units = null;
    if (parameter.StorageType == DB.StorageType.Double)
    {
      units = DB.LabelUtils.GetLabelForUnit(parameter.GetUnitTypeId());
    }

    _parameterDefinitions[key] = new ParameterDefinition(GroupName: group, Units: units);
    return (internalDefinitionName, humanReadableName, group, units);
  }
}
