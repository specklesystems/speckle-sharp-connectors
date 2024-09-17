using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.Revit2023.ToSpeckle.Parameters;

public class ParameterDefinitionHandler
{
  public Dictionary<string, Dictionary<string, object?>> Definitions { get; } = new();

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

#pragma warning disable CA1854 // swapping leads to nullability errors
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

public class ParameterExtractor
{
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ScalingServiceToSpeckle _scalingServiceToSpeckle;

  public ParameterExtractor(IRevitConversionContextStack contextStack, ScalingServiceToSpeckle scalingServiceToSpeckle)
  {
    _parameterDefinitionHandler = contextStack.ParameterDefinitionHandler;
    _contextStack = contextStack;
    _scalingServiceToSpeckle = scalingServiceToSpeckle;
  }

  public Dictionary<string, Dictionary<string, object?>> GetParameters(DB.Element element)
  {
    var paramDict = new Dictionary<string, Dictionary<string, object?>>();

    foreach (DB.Parameter parameter in element.Parameters)
    {
      var (internalDefinitionName, humanReadableName, groupName) = _parameterDefinitionHandler.HandleDefinition(
        parameter
      );
      var param = new Dictionary<string, object?>()
      {
        ["value"] = GetValue(parameter),
        ["name"] = humanReadableName,
        ["internalDefinitionName"] = internalDefinitionName,
      };

      if (!paramDict.TryGetValue(groupName, out Dictionary<string, object?>? paramGroup))
      {
        paramGroup = new Dictionary<string, object?>();
        paramDict[groupName] = paramGroup;
      }

      var targetKey = humanReadableName;
      if (paramGroup.ContainsKey(humanReadableName))
      {
        targetKey = internalDefinitionName;
      }

      paramGroup[targetKey] = param;
    }

    return paramDict;
  }

  private readonly Dictionary<DB.ElementId, string?> _elementNameCache = new();

  private object? GetValue(DB.Parameter parameter)
  {
    switch (parameter.StorageType)
    {
      case DB.StorageType.Double:
        return _scalingServiceToSpeckle.Scale(parameter.AsDouble(), parameter.GetUnitTypeId());
      case DB.StorageType.Integer:
        return parameter.AsInteger();
      case DB.StorageType.ElementId:
        var elId = parameter.AsElementId()!;
        if (_elementNameCache.TryGetValue(elId, out string? value))
        {
          return value;
        }
        var docElement = _contextStack.Current.Document.GetElement(elId);
        var docElementName = docElement?.Name;
        _elementNameCache[parameter.AsElementId()] = docElementName;
        return docElementName;
      case DB.StorageType.String:
      default:
        return parameter.AsString();
    }
  }
}
