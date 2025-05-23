using System.Diagnostics.CodeAnalysis;
using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Services;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.Helpers;

// POC: needs reviewing, it may be fine, not sure how open/closed it is
// really if we have to edit a switch statement...
// maybe also better as an extension method, but maybe is fine?
// POC: there are a lot of public methods here. Maybe consider consolodating
public class ParameterValueExtractor
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly Dictionary<string, HashSet<BuiltInParameter>> _uniqueIdToUsedParameterSetMap = new();

  public ParameterValueExtractor(ScalingServiceToSpeckle scalingService)
  {
    _scalingService = scalingService;
  }

  public object? GetValue(Parameter parameter)
  {
    if (!parameter.HasValue)
    {
      return null;
    }

    return parameter.StorageType switch
    {
      StorageType.Double => GetValueAsDouble(parameter),
      StorageType.Integer => GetValueAsInt(parameter),
      StorageType.String => GetValueAsString(parameter),
      StorageType.ElementId => GetValueAsElementNameOrId(parameter),
      StorageType.None
      or _
        => throw new ValidationException($"Unsupported parameter storage type {parameter.StorageType}")
    };
  }

  public double GetValueAsDouble(Element element, BuiltInParameter builtInParameter)
  {
    if (!TryGetValueAsDouble(element, builtInParameter, out double? value))
    {
      throw new ValidationException($"Failed to get {builtInParameter} as double.");
    }

    return value!.Value; // If TryGet returns true, we succeeded in obtaining the value, and it will not be null.
  }

  public bool TryGetValueAsDouble(Element element, BuiltInParameter builtInParameter, out double? value)
  {
    var number = GetValueGeneric<double?>(
      element,
      builtInParameter,
      StorageType.Double,
      (parameter) => _scalingService.Scale(parameter.AsDouble(), parameter.GetUnitTypeId())
    );
    if (number.HasValue)
    {
      value = number.Value;
      return true;
    }

    value = default;
    return false;
  }

  private double? GetValueAsDouble(Parameter parameter)
  {
    return GetValueGeneric<double?>(
      parameter,
      StorageType.Double,
      (parameter) => _scalingService.Scale(parameter.AsDouble(), parameter.GetUnitTypeId())
    );
  }

  public int GetValueAsInt(Element element, BuiltInParameter builtInParameter)
  {
    return GetValueGeneric<int?>(element, builtInParameter, StorageType.Integer, (parameter) => parameter.AsInteger())
      ?? throw new SpeckleException(
        $"Expected int but got null for property {builtInParameter} on element of type {element.GetType()}"
      );
  }

  private int? GetValueAsInt(Parameter parameter)
  {
    return GetValueGeneric<int?>(parameter, StorageType.Integer, (parameter) => parameter.AsInteger());
  }

  public bool? GetValueAsBool(Element element, BuiltInParameter builtInParameter)
  {
    var intVal = GetValueGeneric<int?>(
      element,
      builtInParameter,
      StorageType.Integer,
      (parameter) => parameter.AsInteger()
    );

    return intVal.HasValue ? Convert.ToBoolean(intVal.Value) : null;
  }

  public string? GetValueAsString(Element element, BuiltInParameter builtInParameter)
  {
    return GetValueGeneric(element, builtInParameter, StorageType.String, (parameter) => parameter.AsString());
  }

  private string? GetValueAsString(Parameter parameter)
  {
    return GetValueGeneric(parameter, StorageType.String, (parameter) => parameter.AsString());
  }

  public bool TryGetValueAsElementId(
    Element element,
    BuiltInParameter builtInParameter,
    [NotNullWhen(true)] out ElementId? elementId
  )
  {
    if (
      GetValueGeneric(element, builtInParameter, StorageType.ElementId, (parameter) => parameter.AsElementId())
      is ElementId elementIdNotNull
    )
    {
      elementId = elementIdNotNull;
      return true;
    }

    elementId = null;
    return false;
  }

  public string? GetValueAsElementNameOrId(Parameter parameter)
  {
    if (
      GetValueGeneric(parameter, StorageType.ElementId, (parameter) => parameter.AsElementId()) is ElementId elementId
    )
    {
      Element element = parameter.Element.Document.GetElement(elementId);
      return element?.Name ?? elementId.ToString();
    }

    return null;
  }

  public bool TryGetValueAsDocumentObject<T>(
    Element element,
    BuiltInParameter builtInParameter,
    [NotNullWhen(true)] out T? value
  )
    where T : class
  {
    if (!TryGetValueAsElementId(element, builtInParameter, out var elementId))
    {
      value = default;
      return false;
    }

    Element paramElement = element.Document.GetElement(elementId.NotNull());
    if (paramElement is not T typedElement)
    {
      value = null;
      return false;
    }

    value = typedElement;
    return true;
  }

  public T GetValueAsDocumentObject<T>(Element element, BuiltInParameter builtInParameter)
    where T : class
  {
    if (!TryGetValueAsDocumentObject<T>(element, builtInParameter, out var value))
    {
      throw new SpeckleException($"Failed to get {builtInParameter} as an element of type {typeof(T)}");
    }

    return value;
  }

  private TResult? GetValueGeneric<TResult>(
    Element element,
    BuiltInParameter builtInParameter,
    StorageType expectedStorageType,
    Func<DB.Parameter, TResult> getParamValue
  )
  {
    if (!_uniqueIdToUsedParameterSetMap.TryGetValue(element.UniqueId, out HashSet<BuiltInParameter>? usedParameters))
    {
      usedParameters = new();
      _uniqueIdToUsedParameterSetMap[element.UniqueId] = usedParameters;
    }
    usedParameters.Add(builtInParameter);
    var parameter = element.get_Parameter(builtInParameter);
    return GetValueGeneric(parameter, expectedStorageType, getParamValue);
  }

  private TResult? GetValueGeneric<TResult>(
    Parameter parameter,
    StorageType expectedStorageType,
    Func<DB.Parameter, TResult> getParamValue
  )
  {
    if (parameter == null || !parameter.HasValue)
    {
      return default;
    }

    if (parameter.StorageType != expectedStorageType)
    {
      throw new SpeckleException(
        $"Expected parameter of storage type {expectedStorageType} but got parameter of storage type {parameter.StorageType}"
      );
    }

    return getParamValue(parameter);
  }

  public Dictionary<string, Parameter> GetAllRemainingParams(DB.Element revitElement)
  {
    var allParams = new Dictionary<string, Parameter>();
    AddElementParamsToDict(revitElement, allParams);

    return allParams;
  }

  private void AddElementParamsToDict(DB.Element element, Dictionary<string, Parameter> paramDict)
  {
    _uniqueIdToUsedParameterSetMap.TryGetValue(element.UniqueId, out HashSet<BuiltInParameter>? usedParameters);

    using var parameters = element.Parameters;
    foreach (DB.Parameter param in parameters)
    {
      var internalName = param.GetInternalName();
      if (paramDict.ContainsKey(internalName))
      {
        continue;
      }

      if (param.GetBuiltInParameter() is BuiltInParameter bip && (usedParameters?.Contains(bip) ?? false))
      {
        continue;
      }

      paramDict[internalName] = param;
    }
  }
}
