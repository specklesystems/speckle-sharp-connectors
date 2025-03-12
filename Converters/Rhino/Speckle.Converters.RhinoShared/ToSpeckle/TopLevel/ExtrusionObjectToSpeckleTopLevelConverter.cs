using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(ExtrusionObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ExtrusionObjectToSpeckleTopLevelConverter
  : IToSpeckleTopLevelConverter,
    ITypedConverter<RG.Extrusion, SOG.ExtrusionX>
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public ExtrusionObjectToSpeckleTopLevelConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {
    var extrusionObject = (ExtrusionObject)target;
    var extrusionEncoding = RawEncodingCreator.Encode(extrusionObject.Geometry, _settingsStore.Current.Document);

    var mesh = DisplayMeshExtractor.GetDisplayMesh(extrusionObject);
    var displayValue = new List<SOG.Mesh> { _meshConverter.Convert(mesh) };

    var bx = new SOG.ExtrusionX()
    {
      displayValue = displayValue,
      encodedValue = extrusionEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }

  public Base ConvertRawExtrusion(RG.Extrusion extrusion) // POC: hate this right now
  {
    var extrusionEncoding = RawEncodingCreator.Encode(extrusion, _settingsStore.Current.Document);

    var mesh = DisplayMeshExtractor.GetDisplayMeshFromGeometry(extrusion);
    var displayValue = new List<SOG.Mesh> { _meshConverter.Convert(mesh) };

    var bx = new SOG.ExtrusionX()
    {
      displayValue = displayValue,
      encodedValue = extrusionEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }

  public SOG.ExtrusionX Convert(RG.Extrusion target) => (SOG.ExtrusionX)ConvertRawExtrusion(target);
}
