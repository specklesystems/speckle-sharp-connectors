using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BrepObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
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

  public Base Convert(object target)
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

    return bx;
  }
}
