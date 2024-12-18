using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(BrepObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public BrepObjectToSpeckleTopLevelConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target)
  {
    var brepObject = (BrepObject)target;
    var brepEncoding = RawEncodingCreator.Encode(brepObject.Geometry, _settingsStore.Current.Document);

    var mesh = DisplayMeshExtractor.GetDisplayMesh(brepObject);
    var displayValue = new List<SOG.Mesh> { _meshConverter.Convert(mesh) };

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return BaseResult.Success(bx);
  }
}
