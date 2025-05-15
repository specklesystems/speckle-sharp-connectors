using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class HatchToSpeckleConverter : ITypedConverter<RG.Hatch, SOG.Region>
{
  private readonly ITypedConverter<RG.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public HatchToSpeckleConverter(
    ITypedConverter<RG.Curve, ICurve> curveConverter,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Hatch geometry to a Speckle Region object.
  /// </summary>
  /// <param name="target">The Hatch to convert.</param>
  /// <returns>The converted Speckle Region object.</returns>
  public SOG.Region Convert(RG.Hatch target)
  {
    // get boundary and inner curves
    RG.Curve rhinoBoundary = target.Get3dCurves(true)[0];
    RG.Curve[] rhinoLoops = target.Get3dCurves(false);

    ICurve boundary = _curveConverter.Convert(rhinoBoundary);
    List<ICurve> innerLoops = rhinoLoops.Select(x => _curveConverter.Convert(x)).ToList();

    // create display mesh from region by converting to brep first
    var brep = RG.Brep.TryConvertBrep(target);

    // Extracting and converting meshes
    // 1. If needed, move geometry to origin before all operations; extract Rhino Mesh
    RG.Mesh movedDisplayMesh = DisplayMeshExtractor.MoveToOriginAndGetDisplayMesh(
      brep,
      _settingsStore.Current.ModelFarFromOrigin,
      out RG.Vector3d? vectorToGeometry
    );

    // 2. Convert extracted Mesh to Speckle. We don't move geometry back yet, because 'far from origin' geometry is causing Speckle conversion issues too
    List<SOG.Mesh> displayValue = new() { _meshConverter.Convert(movedDisplayMesh) };

    // 3. Move Speckle geometry back from origin, if translation was applied
    DisplayMeshExtractor.MoveSpeckleMeshes(displayValue, vectorToGeometry, _settingsStore.Current.SpeckleUnits);

    return new SOG.Region
    {
      boundary = boundary,
      innerLoops = innerLoops,
      hasHatchPattern = true,
      units = _settingsStore.Current.SpeckleUnits,
      displayValue = displayValue
    };
  }
}
