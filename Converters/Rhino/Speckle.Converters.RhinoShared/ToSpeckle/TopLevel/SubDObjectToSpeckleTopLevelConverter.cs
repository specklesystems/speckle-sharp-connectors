using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(SubDObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public SubDObjectToSpeckleTopLevelConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target)
  {
    var subDObject = (SubDObject)target;
    var subdEncoding = RawEncodingCreator.Encode(subDObject.Geometry, _settingsStore.Current.Document);

    var mesh = DisplayMeshExtractor.GetDisplayMesh(subDObject);
    var displayValue = new List<SOG.Mesh> { _meshConverter.Convert(mesh) };

    var bx = new SOG.SubDX()
    {
      displayValue = displayValue,
      encodedValue = subdEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return BaseResult.Success(bx);
  }
}
