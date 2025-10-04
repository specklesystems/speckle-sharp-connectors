using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.ToSpeckle.Properties;

/// <summary>
/// Extracts custom parameters from Revit materials. Expect to be scoped per operation.
/// </summary>
/// <remarks>
/// NOTE: this is inefficient, we're slapping these props to multiple instances.
/// Nothing would make me happier if we can get around this. Until we have a better properties approach, this serves
/// as an INTERIM (hopefully) approach.
/// </remarks>
public class CustomMaterialParametersExtractor
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly Dictionary<string, Dictionary<string, object?>> _customParametersCache = [];

  public CustomMaterialParametersExtractor(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Attempts to get custom parameter properties, using cached values if available.
  /// </summary>
  public Dictionary<string, object?> TryGetCustomParameters(DB.ElementId materialId)
  {
    string materialIdString = materialId.ToString();

    if (_customParametersCache.TryGetValue(materialIdString, out var cachedParameters))
    {
      return cachedParameters;
    }

    var extractedParameters = ExtractCustomParameters(materialId);
    _customParametersCache[materialIdString] = extractedParameters;
    return extractedParameters;
  }

  private Dictionary<string, object?> ExtractCustomParameters(DB.ElementId materialId)
  {
    var customParams = new Dictionary<string, object?>();

    if (_converterSettings.Current.Document.GetElement(materialId) is not DB.Material material)
    {
      return customParams;
    }

    var customParameters = material.Parameters.Cast<DB.Parameter>().Where(IsCustomParameter);

    foreach (DB.Parameter customParameter in customParameters)
    {
      customParams[customParameter.Definition.Name] = ExtractParameterValue(customParameter);
    }

    return customParams;
  }

  private bool IsCustomParameter(DB.Parameter param) =>
    param.Definition is not DB.InternalDefinition internalDef
    || internalDef.BuiltInParameter == DB.BuiltInParameter.INVALID; // ExternalDefinition (shared params) are custom

  private object? ExtractParameterValue(DB.Parameter param) =>
    param.StorageType switch
    {
      DB.StorageType.Double => GetScaledDoubleValue(param),
      DB.StorageType.Integer => CreatePropertyDict(param.Definition.Name, param.AsInteger()),
      DB.StorageType.String => CreatePropertyDict(param.Definition.Name, param.AsString() ?? string.Empty),
      DB.StorageType.ElementId => CreatePropertyDict(param.Definition.Name, param.AsElementId().ToString()),
      _ => null
    };

  private object? GetScaledDoubleValue(DB.Parameter param)
  {
    double rawValue = param.AsDouble();
    var dataType = param.Definition.GetDataType();

    if (dataType == DB.SpecTypeId.Number || dataType == null)
    {
      return CreatePropertyDict(param.Definition.Name, rawValue);
    }

    var unitId = _converterSettings.Current.Document.GetUnits().GetFormatOptions(dataType).GetUnitTypeId();

    return new Dictionary<string, object>
    {
      ["name"] = param.Definition.Name,
      ["value"] = _scalingService.Scale(rawValue, unitId),
      ["units"] = DB.LabelUtils.GetLabelForUnit(unitId)
    };
  }

  private Dictionary<string, object>? CreatePropertyDict(string name, object value) =>
    new() { ["name"] = name, ["value"] = value };
}
