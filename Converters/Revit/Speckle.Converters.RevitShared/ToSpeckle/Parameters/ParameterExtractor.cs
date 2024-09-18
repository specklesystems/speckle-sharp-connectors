using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
/// </summary>
public class ParameterExtractor
{
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;
  private readonly IConverterSettingsStore<RevitConversionSettings> _settingsStore;
  private readonly ScalingServiceToSpeckle _scalingServiceToSpeckle;

  public ParameterExtractor(
    IConverterSettingsStore<RevitConversionSettings> settingsStore,
    ScalingServiceToSpeckle scalingServiceToSpeckle,
    ParameterDefinitionHandler parameterDefinitionHandler
  )
  {
    _parameterDefinitionHandler = parameterDefinitionHandler;
    _settingsStore = settingsStore;
    _scalingServiceToSpeckle = scalingServiceToSpeckle;
  }

  /// <summary>
  /// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
  /// </summary>
  /// <param name="element"></param>
  /// <returns></returns>
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
        ["internalDefinitionName"] = internalDefinitionName
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
        var docElement = _settingsStore.Current.Document.GetElement(elId);
        var docElementName = docElement?.Name;
        _elementNameCache[parameter.AsElementId()] = docElementName;
        return docElementName;
      case DB.StorageType.String:
      default:
        return parameter.AsString();
    }
  }
}
