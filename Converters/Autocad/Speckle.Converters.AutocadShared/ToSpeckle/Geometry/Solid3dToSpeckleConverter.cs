using Speckle.Converters.Autocad.ToSpeckle.Encoding;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts AutoCAD Solid3d entities to SolidX with SAT encoding for lossless round-trip.
/// </summary>
[NameAndRankValue(typeof(ADB.Solid3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 1)]
public class Solid3dToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public Solid3dToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> meshConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => RawConvert((ADB.Solid3d)target);

  public SOG.SolidX RawConvert(ADB.Solid3d target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    // Generate display meshes for viewer
    List<SOG.Mesh> displayValue = DisplayMeshExtractor.GetSpeckleMeshes(target, _meshConverter);

    // Calculate geometric properties - tbd
    double volume = 0;
    double area = 0;

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
    catch (System.Exception ex)
    {
      throw new ConversionException($"Failed to calculate geometric properties: {ex.Message}", ex);
    }

    // Create raw encoding for round-tripping
    var encoding = RawEncodingCreator.Encode(target);

    return new SOG.SolidX
    {
      displayValue = displayValue,
      encodedValue = encoding,
      volume = volume,
      area = area,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
