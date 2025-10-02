using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models;

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

  private readonly Dictionary<DB.ElementId, Dictionary<string, Dictionary<string, object?>>> _systemTypeParameterCache =
    new();

  /// <summary>
  /// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
  /// </summary>
  /// <param name="element"></param>
  /// <returns></returns>
  public Base GetParameters(DB.Element element)
  {
    var instanceParams = new Base();
    instanceParams["instanceParameters"] = ParseParameterSet(element.Parameters);
    var typeParams = new Base();
    typeParams["typeParameters"] = GetTypeParameterDictionary(element);
    // NOTE: Woe and despair, I'm really abusing dictionaries here. See note at the top of class.
    var props = new Base();
    props["@Instance Parameters"] = instanceParams;
    props["@Type Parameters"] = typeParams;
    props["System Type Parameters"] = GetSystemTypeParameterDictionary(element);
    // return new Dictionary<string, object?>()
    // {
    //   ["@Instance Parameters"] = instanceParams,
    //   ["@Type Parameters"] = typeParams,
    //   ["System Type Parameters"] = GetSystemTypeParameterDictionary(element)
    // };
    return props;
  }

  private Dictionary<string, Dictionary<string, object?>>? GetTypeParameterDictionary(DB.Element element)
  {
    var typeId = element.GetTypeId();
    if (typeId == DB.ElementId.InvalidElementId)
    {
      return null;
    }

    if (
      _typeParameterCache.TryGetValue(
        typeId,
        out Dictionary<string, Dictionary<string, object?>>? typeParameterDictionary
      )
    )
    {
      return typeParameterDictionary;
    }

    if (_settingsStore.Current.Document.GetElement(typeId) is not DB.ElementType type)
    {
      return null;
    }

    typeParameterDictionary = ParseParameterSet(type.Parameters); // NOTE: type parameters should be ideally proxied out for a better data layout.
    if (type is DB.HostObjAttributes hostObjectAttr)
    {
      // NOTE: this could be paired up and merged with material quantities - they're pretty much the same :/
      var factor = _scalingServiceToSpeckle.ScaleLength(1);
      if (hostObjectAttr.GetCompoundStructure() is DB.CompoundStructure structure) // GetCompoundStructure can return null
      {
        Dictionary<string, object?> structureDictionary = new();
        foreach (var layer in structure.GetLayers())
        {
          if (_settingsStore.Current.Document.GetElement(layer.MaterialId) is DB.Material material)
          {
            var uniqueLayerName = $"{material.Name} ({layer.LayerId})";
            structureDictionary[uniqueLayerName] = new Dictionary<string, object>()
            {
              ["material"] = material.Name,
              ["function"] = layer.Function.ToString(),
              ["thickness"] = layer.Width * factor,
              ["units"] = _settingsStore.Current.SpeckleUnits
            };
          }
        }

        typeParameterDictionary["Structure"] = structureDictionary;
      }
    }

    _typeParameterCache[typeId] = typeParameterDictionary;
    return typeParameterDictionary;
  }

  private Dictionary<string, Dictionary<string, object?>>? GetSystemTypeParameterDictionary(DB.Element element)
  {
    DB.MEPSystem? system = GetMEPSystem(element);

    if (system != null)
    {
      DB.ElementId systemTypeId = system.GetTypeId();

      if (
        _systemTypeParameterCache.TryGetValue(
          systemTypeId,
          out Dictionary<string, Dictionary<string, object?>>? systemTypeParameterDictionary
        )
      )
      {
        return systemTypeParameterDictionary;
      }

      DB.Element systemType = _settingsStore.Current.Document.GetElement(systemTypeId);
      systemTypeParameterDictionary = ParseParameterSet(systemType.Parameters);
      _systemTypeParameterCache[systemTypeId] = systemTypeParameterDictionary;
      return systemTypeParameterDictionary;
    }

    return null;
  }

  private DB.MEPSystem? GetMEPSystem(DB.Element element)
  {
    if (element is DB.MEPCurve curve)
    {
      return curve.MEPSystem;
    }

    if (element is DB.FamilyInstance fi)
    {
      var cm = fi.MEPModel?.ConnectorManager;
      if (cm != null)
      {
        foreach (DB.Connector conn in cm.Connectors)
        {
          if (conn.ConnectorType == DB.ConnectorType.Physical && conn.IsConnected && conn.MEPSystem != null)
          {
            return conn.MEPSystem;
          }
        }
      }
    }

    return null;
  }

  private Dictionary<string, Dictionary<string, object?>> ParseParameterSet(DB.ParameterSet parameters)
  {
    var dict = new Dictionary<string, Dictionary<string, object?>>();
    foreach (DB.Parameter parameter in parameters)
    {
      try
      {
        var (internalDefinitionName, humanReadableName, groupName, units) =
          _parameterDefinitionHandler.HandleDefinition(parameter);

        // NOTE: general assumption is that ids don't really have much meaning. See [CNX-556: All ID Parameters are send as Name](https://linear.app/speckle/issue/CNX-556/all-id-parameters-are-send-as-name)
        // NOTE: subsequent request resulting in certain IDs being brought back. See [CNX-1125](https://linear.app/speckle/issue/CNX-1125/publish-type-id-instead-of-name) in GetValue() method
        // "Type ID" (associated with "SYMBOL_ID_PARAM") won't evaluate to true here which is intentional
        // NOTE: It seems we are skipping Type Mark parameter because it is called WINDOW_TYPE_ID internally (WTF ADSK).
        if (internalDefinitionName.EndsWith("_ID") && !internalDefinitionName.Equals("WINDOW_TYPE_ID"))
        {
          continue;
        }

        // NOTE: excepted behaviour is to use GetValue BUT if we have "SYMBOL_ID_PARAM", we just want id
        // see above comment and linked linear ticket / issue.
        object? value =
          internalDefinitionName == "SYMBOL_ID_PARAM" ? parameter.AsElementId().ToString() : GetValue(parameter);

        var isNullOrEmpty = value == null || (value is string s && string.IsNullOrEmpty(s));

        if (!_settingsStore.Current.SendParameterNullOrEmptyStrings && isNullOrEmpty)
        {
          continue;
        }

        if (value is (string typeName, string familyName)) // element type: same element, different expected values depending on the param definition
        {
          if (internalDefinitionName == "ELEM_FAMILY_PARAM") // Probably should be using the BUILTINPARAM whatever
          {
            value = familyName;
          }
          else if (internalDefinitionName == "ELEM_TYPE_PARAM")
          {
            value = typeName;
          }
          else
          {
            value = familyName + " " + typeName;
          }
        }

        var param = new Dictionary<string, object?>()
        {
          ["value"] = value,
          ["name"] = humanReadableName,
          ["internalDefinitionName"] = internalDefinitionName
        };

        if (units is not null)
        {
          param["units"] = units;
        }

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
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Failed to convert parameter {parameterDefinitionName}", parameter.Definition.Name);
      }
    }

    return dict;
  }

  private readonly Dictionary<DB.ElementId, object?> _elementNameCache = new();

  private object? GetValue(DB.Parameter parameter)
  {
    switch (parameter.StorageType)
    {
      case DB.StorageType.Double:
        return _scalingServiceToSpeckle.Scale(parameter.AsDouble(), parameter.GetUnitTypeId());
      case DB.StorageType.Integer:
        var integer = parameter.AsInteger();
        var valueString = parameter.AsValueString();
        if (integer.ToString() == valueString)
        {
          return integer;
        }
        else
        {
          return valueString;
        }

      case DB.StorageType.ElementId:
        var elId = parameter.AsElementId()!;
        if (elId == DB.ElementId.InvalidElementId)
        {
          return null;
        }

        if (_elementNameCache.TryGetValue(elId, out object? value))
        {
          return value;
        }

        var docElement = _settingsStore.Current.Document.GetElement(elId);
        object? docElementName;

        // Note: for element types, different params point at the same element. We're getting the right value out in the parent function
        // based on what the actual built in param name is.
        if (docElement is DB.ElementType elementType)
        {
          docElementName = (elementType.Name, elementType.FamilyName);
        }
        else
        {
          docElementName = docElement?.Name ?? null;
        }

        _elementNameCache[parameter.AsElementId()] = docElementName;
        return docElementName;
      case DB.StorageType.String:
      default:
        return parameter.AsString();
    }
  }
}
