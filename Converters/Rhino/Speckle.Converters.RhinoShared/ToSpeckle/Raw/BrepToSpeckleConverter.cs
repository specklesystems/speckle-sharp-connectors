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

    // this is not pretty, but this way we avoid injecting another dependency unnecessarily
    List<SOG.Mesh> displayValue = ToSpeckleMeshUtility.GetSpeckleMeshes(
      target,
      _settingsStore.Current.ModelFarFromOrigin,
      _settingsStore.Current.SpeckleUnits,
      _meshConverter
    );

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }
}
