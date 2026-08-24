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

  // All three caches are keyed by UniqueId, NOT ElementId. An ElementId is only unique WITHIN a document, and this
  // extractor is scoped to the whole send operation — host document plus every linked model — so an ElementId key
  // served a linked type's parameters from whichever document got there first.
  private readonly Dictionary<string, Dictionary<string, Dictionary<string, object?>>> _typeParameterCache = new();

  private readonly Dictionary<string, Dictionary<string, Dictionary<string, object?>>> _systemTypeParameterCache =
    new();

  /// <summary>
  /// Extracts parameters out from an element and populates the <see cref="ParameterDefinitionHandler"/> cache. Expects to be scoped per operation.
  /// </summary>
  /// <param name="element"></param>
  /// <returns></returns>
  public Dictionary<string, object?> GetParameters(DB.Element element)
  {
    // NOTE: Woe and despair, I'm really abusing dictionaries here. See note at the top of class.
    return new Dictionary<string, object?>()
    {
      ["Instance Parameters"] = ParseParameterSet(element.Parameters),
      ["Type Parameters"] = GetTypeParameterDictionary(element),
      ["System Type Parameters"] = GetSystemTypeParameterDictionary(element),
    };
  }

  private Dictionary<string, Dictionary<string, object?>>? GetTypeParameterDictionary(DB.Element element)
  {
    var typeId = element.GetTypeId();
    if (typeId == DB.ElementId.InvalidElementId)
    {
      return null;
    }

    if (_settingsStore.Current.Document.GetElement(typeId) is not DB.ElementType type)
    {
      return null;
    }

    if (
      _typeParameterCache.TryGetValue(
        type.UniqueId,
        out Dictionary<string, Dictionary<string, object?>>? typeParameterDictionary
      )
    )
    {
      return typeParameterDictionary;
    }

    // NOTE: compound structure used to be grafted on here as a pseudo type parameter. It now lives at the top of
    // `properties` beside Material Quantities — see CompoundStructureExtractor [ENG-9338].
    typeParameterDictionary = ParseParameterSet(type.Parameters); // NOTE: type parameters should be ideally proxied out for a better data layout.
    EnsureTypeName(type, typeParameterDictionary);

    _typeParameterCache[type.UniqueId] = typeParameterDictionary;
    return typeParameterDictionary;
  }

  /// <summary>
  /// Guarantees the Identity Data ▸ Type Name entry that ODA's <c>.rvt</c> reader always publishes, so the two
  /// producers agree [ENG-8684]. <c>ALL_MODEL_TYPE_NAME</c> is read-only, is not reliably enumerated by
  /// <c>ElementType.Parameters</c>, and a null <c>AsString()</c> is discarded unless
  /// <c>SendParameterNullOrEmptyStrings</c> is on — so today it arrives by luck. A real parameter value always wins.
  /// The group label is resolved the same way its siblings' are, so the entry lands inside "Identitätsdaten" with
  /// them on a German install rather than stranding a lone English group.
  /// </summary>
  private void EnsureTypeName(DB.ElementType type, Dictionary<string, Dictionary<string, object?>> typeParameters)
  {
    string humanReadableName = "Type Name";
    string groupName;

    if (type.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_NAME) is DB.Parameter parameter)
    {
      (_, humanReadableName, groupName, _) = _parameterDefinitionHandler.HandleDefinition(parameter);
    }
    else
    {
      groupName = DB.LabelUtils.GetLabelForGroup(DB.GroupTypeId.IdentityData);
    }

    if (!typeParameters.TryGetValue(groupName, out Dictionary<string, object?>? group))
    {
      group = new Dictionary<string, object?>();
      typeParameters[groupName] = group;
    }

    // ParseParameterSet already got a real value out of Revit — leave it exactly as it found it.
    if (
      group.TryGetValue(humanReadableName, out var existing)
      && existing is Dictionary<string, object?> record
      && record.TryGetValue("value", out var existingValue)
      && existingValue is string existingName
      && !string.IsNullOrEmpty(existingName)
    )
    {
      return;
    }

    group[humanReadableName] = new Dictionary<string, object?>()
    {
      ["value"] = type.Name,
      ["name"] = humanReadableName,
      ["internalDefinitionName"] = "ALL_MODEL_TYPE_NAME",
    };
  }

  private Dictionary<string, Dictionary<string, object?>>? GetSystemTypeParameterDictionary(DB.Element element)
  {
    DB.MEPSystem? system = GetMEPSystem(element);

    if (system != null)
    {
      DB.Element systemType = _settingsStore.Current.Document.GetElement(system.GetTypeId());

      if (
        _systemTypeParameterCache.TryGetValue(
          systemType.UniqueId,
          out Dictionary<string, Dictionary<string, object?>>? systemTypeParameterDictionary
        )
      )
      {
        return systemTypeParameterDictionary;
      }

      systemTypeParameterDictionary = ParseParameterSet(systemType.Parameters);
      _systemTypeParameterCache[systemType.UniqueId] = systemTypeParameterDictionary;
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
          ["internalDefinitionName"] = internalDefinitionName,
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

  private readonly Dictionary<string, object?> _elementNameCache = new();

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
        var elId = parameter.AsElementId();
        if (elId == DB.ElementId.InvalidElementId)
        {
          return null;
        }

        var docElement = _settingsStore.Current.Document.GetElement(elId);
        if (docElement is null)
        {
          return null;
        }

        if (_elementNameCache.TryGetValue(docElement.UniqueId, out object? value))
        {
          return value;
        }

        object? docElementName;

        // Note: for element types, different params point at the same element. We're getting the right value out in the parent function
        // based on what the actual built in param name is.
        if (docElement is DB.ElementType elementType)
        {
          docElementName = (elementType.Name, elementType.FamilyName);
        }
        else
        {
          docElementName = docElement.Name;
        }

        _elementNameCache[docElement.UniqueId] = docElementName;
        return docElementName;
      case DB.StorageType.String:
      default:
        return parameter.AsString();
    }
  }
}
