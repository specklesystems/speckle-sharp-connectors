using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts AutoCAD Solid3d entities to SolidX with SAT encoding for lossless round-trip.
/// </summary>
[NameAndRankValue(typeof(ADB.Solid3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 1)]
public class Solid3dToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<ADB.Solid3d, RawEncoding> _encodingConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public Solid3dToSpeckleConverter(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> meshConverter,
    ITypedConverter<ADB.Solid3d, RawEncoding> encodingConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _encodingConverter = encodingConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => RawConvert((ADB.Solid3d)target);

  public SOG.SolidX RawConvert(ADB.Solid3d target)
  {
    // Generate display mesh for viewer
    SOG.Mesh displayMesh = _meshConverter.Convert(target);

    // Calculate geometric properties
    double? volume = null;
    double? area = null;

    try
    {
      using ABR.Brep brep = new(target);
      if (!brep.IsNull)
      {
        area = brep.GetSurfaceArea();
        try
        {
          volume = brep.GetVolume();
        }
        catch (ABR.Exception)
        {
          // Volume calculation can fail for non-volumetric solids
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      throw new ConversionException($"Failed to calculate geometric properties: {ex.Message}", ex);
    }

    // Create raw encoding for round-tripping
    RawEncoding encoding = _encodingConverter.Convert(target);

    return new SOG.SolidX
    {
      displayValue = [displayMesh],
      encodedValue = encoding,
      volume = volume,
      area = area,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
