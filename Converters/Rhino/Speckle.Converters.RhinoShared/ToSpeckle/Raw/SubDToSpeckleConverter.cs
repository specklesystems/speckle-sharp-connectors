using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class SubDToSpeckleConverter : ITypedConverter<RG.SubD, SOG.SubDX>
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public SubDToSpeckleConverter(
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a SubD geometry to a SpeckleSubDX object.
  /// </summary>
  /// <param name="target">The SubD to convert.</param>
  /// <returns>The converted Speckle SubDX object.</returns>
  public SOG.SubDX Convert(RG.SubD target)
  {
    var subdEncoding = RawEncodingCreator.Encode(target, _settingsStore.Current.Document);

    List<SOG.Mesh> displayValue = DisplayMeshExtractor.GetSpeckleMeshes(
      target,
      _meshConverter,
      _settingsStore.Current.Document
    );

    var bx = new SOG.SubDX()
    {
      displayValue = displayValue,
      encodedValue = subdEncoding,
      units = _settingsStore.Current.SpeckleUnits,
    };

    return bx;
  }
}
