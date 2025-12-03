using Speckle.Converters.Autocad.ToSpeckle.Encoding;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts AutoCAD Solid3d entities to DataObject with SAT encoding for lossless round-trip.
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

  public DataObject RawConvert(ADB.Solid3d target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    var encoding = RawEncodingCreator.Encode(target);

    // Generate display meshes for viewer
    List<SOG.Mesh> displayValue = DisplayMeshExtractor.GetSpeckleMeshes(target, _meshConverter);

    string typeName = target.GetType().Name;

    var dataObject = new DataObject
    {
      name = typeName,
      displayValue = displayValue.Cast<Base>().ToList(),
      properties = new Dictionary<string, object?>(),
      applicationId = target.Handle.Value.ToString()
    };

    // Attach SAT encoding to DataObject
    dataObject["encodedValue"] = encoding;
    dataObject["units"] = _settingsStore.Current.SpeckleUnits;

    return dataObject;
  }
}
