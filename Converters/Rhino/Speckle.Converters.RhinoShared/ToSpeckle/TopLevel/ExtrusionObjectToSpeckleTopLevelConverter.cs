using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(ExtrusionObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ExtrusionObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public ExtrusionObjectToSpeckleTopLevelConverter( 
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore)
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
}
