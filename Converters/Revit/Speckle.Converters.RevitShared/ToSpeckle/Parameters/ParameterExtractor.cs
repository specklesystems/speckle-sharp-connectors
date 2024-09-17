using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.Revit2023.ToSpeckle.Parameters;

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
