using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.Revit2023.ToSpeckle.Properties;

public class StructuralMaterialAssetExtractor
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public StructuralMaterialAssetExtractor(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Gets the density of a structural asset and its accompanying units.
  /// </summary>
  /// <remarks>
  /// Scaled from internal units to model units
  /// </remarks>
  public (double density, string units)? GetDensity(DB.ElementId structuralAssetId)
  {
    // NOTE: assetId != DB.ElementId.InvalidElementId checked in calling method. Assuming a valid StructuralAssetId
    if (
      _converterSettings.Current.Document.GetElement(structuralAssetId) is not DB.PropertySetElement propertySetElement
    )
    {
      return null;
    }
    DB.StructuralAsset structuralAsset = propertySetElement.GetStructuralAsset();

    // get unit forge type id
    DB.ForgeTypeId densityUnitId = _converterSettings
      .Current.Document.GetUnits()
      .GetFormatOptions(DB.SpecTypeId.MassDensity)
      .GetUnitTypeId();

    // get units string
    string? densityUnitString = densityUnitId.ToString();

    if (string.IsNullOrEmpty(densityUnitString))
    {
      return null; // .ToString() is nullable and if the units string is null I wouldn't trust the scaling
    }

    // scale from internal to model units
    double densityValue = _scalingService.Scale(structuralAsset.Density, densityUnitId);

    // return value and units
    return (densityValue, densityUnitString);
  }
}
