using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BrepObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter, ITypedConverter<RG.Brep, SOG.BrepX>
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

  public Base ConvertRawBrep(RG.Brep target) // POC: hate this right now
  {
    var brepEncoding = RawEncodingCreator.Encode(target, _settingsStore.Current.Document);

    var mesh = DisplayMeshExtractor.GetDisplayMeshFromGeometry(target);
    var displayValue = new List<SOG.Mesh> { _meshConverter.Convert(mesh) };

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }

  public SOG.BrepX Convert(RG.Brep target) => (SOG.BrepX)ConvertRawBrep(target);
}
