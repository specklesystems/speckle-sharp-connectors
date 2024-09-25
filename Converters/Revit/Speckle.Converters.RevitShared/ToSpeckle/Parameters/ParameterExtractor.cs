using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
/// </summary>
public class ParameterExtractor
{
  /// POC: Note that we're abusing dictionaries in here because we've yet to have a simple way to serialize non-base derived classes (or structs?)
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;

  private readonly ILogger<ParameterExtractor> _logger;
  private readonly IConverterSettingsStore<RevitConversionSettings> _settingsStore;
  private readonly ScalingServiceToSpeckle _scalingServiceToSpeckle;

  public ParameterExtractor(
    IConverterSettingsStore<RevitConversionSettings> settingsStore,
    ScalingServiceToSpeckle scalingServiceToSpeckle,
    ParameterDefinitionHandler parameterDefinitionHandler,
    ILogger<ParameterExtractor> logger
  )
  {
    _parameterDefinitionHandler = parameterDefinitionHandler;
    _logger = logger;
    _settingsStore = settingsStore;
    _scalingServiceToSpeckle = scalingServiceToSpeckle;
  }

  private readonly Dictionary<DB.ElementId, Dictionary<string, Dictionary<string, object?>>> _typeParameterCache =
    new();

  /// <summary>
  /// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
  /// </summary>
  /// <param name="element"></param>
  /// <returns></returns>
  public Dictionary<string, object?> GetParameters(DB.Element element)
  {
    // NOTE: Woe and despair, I'm really abusing dictionaries here. See note at the top of class.
    var instanceParameterDictionary = ParseParameterSet(element.Parameters);

    var typeId = element.GetTypeId();
    if (typeId == DB.ElementId.InvalidElementId)
    {
      return CreateParameterDictionary(instanceParameterDictionary, null);
    }

    if (
      _typeParameterCache.TryGetValue(
        typeId,
        out Dictionary<string, Dictionary<string, object?>>? typeParameterDictionary
      )
    )
    {
      return CreateParameterDictionary(instanceParameterDictionary, typeParameterDictionary);
    }

    if (_settingsStore.Current.Document.GetElement(typeId) is not DB.ElementType type)
    {
      return CreateParameterDictionary(instanceParameterDictionary, null);
    }

    typeParameterDictionary = ParseParameterSet(type.Parameters);
    _typeParameterCache[typeId] = typeParameterDictionary;

    return CreateParameterDictionary(instanceParameterDictionary, typeParameterDictionary);
  }

  private Dictionary<string, object?> CreateParameterDictionary(
    Dictionary<string, Dictionary<string, object?>> instanceParams,
    Dictionary<string, Dictionary<string, object?>>? typeParams
  )
  {
    return new Dictionary<string, object?>()
    {
      ["Instance Parameters"] = instanceParams,
      ["Type Parameters"] = typeParams
    };
  }

  private Dictionary<string, Dictionary<string, object?>> ParseParameterSet(DB.ParameterSet parameters)
  {
    var dict = new Dictionary<string, Dictionary<string, object?>>();
    foreach (DB.Parameter parameter in parameters)
    {
      try
      {
        var value = GetValue(parameter);
        var isNullOrEmpty = value == null || (value is string s && string.IsNullOrEmpty(s));
        if (!_settingsStore.Current.SendParameterNullOrEmptyStrings && isNullOrEmpty)
        {
          continue;
        }

        var (internalDefinitionName, humanReadableName, groupName) = _parameterDefinitionHandler.HandleDefinition(
          parameter
        );

        var param = new Dictionary<string, object?>()
        {
          ["value"] = value,
          ["name"] = humanReadableName,
          ["internalDefinitionName"] = internalDefinitionName
        };

        if (!dict.TryGetValue(groupName, out Dictionary<string, object?>? paramGroup))
        {
          paramGroup = new Dictionary<string, object?>();
          dict[groupName] = paramGroup;
        }

        var targetKey = humanReadableName;
        if (paramGroup.ContainsKey(humanReadableName))
        {
          targetKey = internalDefinitionName;
        }

        paramGroup[targetKey] = param;
      }
      catch (Exception e) when (!e.IsFatal())
      {
        _logger.LogWarning(e, $"Failed to convert parameter {parameter.Definition.Name}");
      }
    }

    return dict;
  }

  private readonly Dictionary<DB.ElementId, string?> _elementNameCache = new();

  private object? GetValue(DB.Parameter parameter)
  {
    switch (parameter.StorageType)
    {
      case DB.StorageType.Double:
        return _scalingServiceToSpeckle.Scale(parameter.AsDouble(), parameter.GetUnitTypeId());
      case DB.StorageType.Integer:
        return parameter.AsInteger().ToString() == parameter.AsValueString()
          ? parameter.AsInteger()
          : parameter.AsValueString();
      case DB.StorageType.ElementId:
        var elId = parameter.AsElementId()!;
        if (_elementNameCache.TryGetValue(elId, out string? value))
        {
          return value;
        }
        var docElement = _settingsStore.Current.Document.GetElement(elId);
        var docElementName = docElement?.Name ?? elId.ToString();
        _elementNameCache[parameter.AsElementId()] = docElementName;
        return docElementName;
      case DB.StorageType.String:
      default:
        return parameter.AsString();
    }
  }
}
