using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.TopLevel;

/// <summary>
/// Converts AutoCAD Solid3d entities to Civil3dObject with SAT encoding for round-trip.
/// This Civil3D-specific converter overrides the base AutoCAD converter to include property sets.
/// </summary>
[NameAndRankValue(typeof(ADB.Solid3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK + 2)]
public class Solid3dToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<ADB.Solid3d, RawEncoding> _encodingConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly PropertiesExtractor _propertiesExtractor;

  public Solid3dToSpeckleConverter(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> meshConverter,
    ITypedConverter<ADB.Solid3d, RawEncoding> encodingConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor
  )
  {
    _meshConverter = meshConverter;
    _encodingConverter = encodingConverter;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
  }

  public Base Convert(object target) => RawConvert((ADB.Solid3d)target);

  public Civil3dObject RawConvert(ADB.Solid3d target)
  {
    // Generate display mesh for viewer
    SOG.Mesh displayMesh = _meshConverter.Convert(target);

    // Create raw encoding for round-tripping
    RawEncoding encoding = _encodingConverter.Convert(target);

    Dictionary<string, object?> properties = _propertiesExtractor.GetProperties(target);

    string typeName = target.GetType().Name;

    var civilObject = new Civil3dObject
    {
      name = typeName,
      type = typeName,
      displayValue = [displayMesh],
      properties = properties,
      baseCurves = null,
      elements = [],
      units = _settingsStore.Current.SpeckleUnits,
      applicationId = target.Handle.Value.ToString(),
    };

    // Attach SAT encoding for round-trip
    civilObject["encodedValue"] = encoding;

    return civilObject;
  }
}
