using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Encoding;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.DoubleNumerics;

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

    List<SOG.Mesh> displayValue = GetSpeckleMeshes(target, _settingsStore.Current.ModelFarFromOrigin);

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }

  private List<SOG.Mesh> GetSpeckleMeshes(RG.GeometryBase geometry, bool modelFarFromOrigin)
  {
    // get valid Rhino meshes (possibly moved to origin for accurate calculations)
    (RG.Mesh displayMesh, RG.Vector3d? translation) = DisplayMeshExtractor.GetGeometryDisplayMeshAccurate(
      geometry,
      modelFarFromOrigin
    );

    List<SOG.Mesh> displayValue = new() { _meshConverter.Convert(displayMesh) };

    // move Speckle geometry back from origin, if translation was applied. This needs to be done after Speckle conversion,
    // because 'far from origin' precision errors also affect ToSpeckle converters.
    if (translation is RG.Vector3d vector)
    {
      Matrix4x4 matrix = new(1, 0, 0, vector.X, 0, 1, 0, vector.Y, 0, 0, 1, vector.Z, 0, 0, 0, 1);
      SO.Transform transform = new() { matrix = matrix, units = _settingsStore.Current.SpeckleUnits };
      displayValue.ForEach(x => x.Transform(transform));
    }

    return displayValue;
  }
}
