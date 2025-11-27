using Speckle.Converters.Autocad.ToSpeckle.Encoding;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

/// <summary>
/// Converts AutoCAD Solid3d entities to SolidX with DWG encoding.
/// </summary>
public class Solid3dToSolidXRawConverter : ITypedConverter<ADB.Solid3d, SOG.SolidX>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public Solid3dToSolidXRawConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> meshConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public SOG.SolidX Convert(ADB.Solid3d target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    var database = target.Database ?? throw new ConversionException("Solid3d entity must belong to a database.");

    // Create raw encoding for round-tripping
    var solidEncoding = RawEncodingCreator.Encode(target, database);

    // Generate display meshes for viewer
    List<SOG.Mesh> displayValue = DisplayMeshExtractor.GetSpeckleMeshes(target, _meshConverter);

    // Calculate geometric properties
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

    // Create SolidX with all data
    var solidX = new SOG.SolidX
    {
      displayValue = displayValue,
      encodedValue = solidEncoding,
      volume = volume,
      area = area,
      units = _settingsStore.Current.SpeckleUnits
    };

    return solidX;
  }
}
