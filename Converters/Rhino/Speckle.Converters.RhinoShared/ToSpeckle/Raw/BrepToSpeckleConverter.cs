using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class BrepToSpeckleConverter : ITypedConverter<RG.Brep, SOG.BrepX>
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public BrepToSpeckleConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Brep geometry to a Speckle BrepX object.
  /// </summary>
  /// <param name="target">The Brep to convert.</param>
  /// <returns>The converted Speckle BrepX object.</returns>
  public SOG.BrepX Convert(RG.Brep target)
  {
    var brepEncoding = RawEncodingCreator.Encode(target, _settingsStore.Current.Document);

    List<SOG.Mesh> displayValue = DisplayMeshExtractor.GetSpeckleMeshes(
      target,
      _meshConverter,
      _settingsStore.Current.Document
    );

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      volume = target.IsSolid ? target.GetVolume() : 0,
      area = target.GetArea(),
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }
}
