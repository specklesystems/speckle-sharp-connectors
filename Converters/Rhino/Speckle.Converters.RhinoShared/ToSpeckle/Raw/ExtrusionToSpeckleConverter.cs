using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class ExtrusionToSpeckleConverter : ITypedConverter<RG.Extrusion, SOG.ExtrusionX>
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public ExtrusionToSpeckleConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Extrusion geometry to a Speckle ExtrusionX object.
  /// </summary>
  /// <param name="target">The Extrusion to convert.</param>
  /// <returns>The converted Speckle ExtrusionX object.</returns>
  public SOG.ExtrusionX Convert(RG.Extrusion target)
  {
    var extrusionEncoding = RawEncodingCreator.Encode(target, _settingsStore.Current.Document);

    var displayMesh = DisplayMeshExtractor.GetGeometryDisplayMesh(target);
    List<SOG.Mesh> displayValue = displayMesh is null ? new() : new() { _meshConverter.Convert(displayMesh) };

    var bx = new SOG.ExtrusionX()
    {
      displayValue = displayValue,
      encodedValue = extrusionEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }
}
