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

    List<SOG.Mesh> displayValue = GetSpeckleMeshes(target);

    var bx = new SOG.BrepX()
    {
      displayValue = displayValue,
      encodedValue = brepEncoding,
      units = _settingsStore.Current.SpeckleUnits
    };

    return bx;
  }

  private List<SOG.Mesh> GetSpeckleMeshes(RG.GeometryBase geometry)
  {
    (RG.GeometryBase displayMesh, RG.Vector3d? translation) = GetGeometryDisplayMeshAccurate(
      geometry,
      _settingsStore.Current.ModelFarFromOrigin
    );

    List<SOG.Mesh> displayValue = new() { _meshConverter.Convert((RG.Mesh)displayMesh) };
    if (translation is RG.Vector3d vector)
    {
      var matrix = Matrix4x4.CreateTranslation(new Vector3(vector.X, vector.Y, vector.Z));
      SO.Transform transform = new() { matrix = matrix, units = _settingsStore.Current.SpeckleUnits };
      displayValue.ForEach(x => x.Transform(transform));
    }

    return displayValue;
  }

  /// <summary>
  /// Returns the mesh of the geometry, possibly moved to the origin for better accuracy.
  /// </summary>
  private (RG.GeometryBase, RG.Vector3d?) GetGeometryDisplayMeshAccurate(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin
  )
  {
    // preserve original behavior, if Model is not far from origin: will be the case for 99% of Rhino models
    if (!modelFarFromOrigin)
    {
      return (DisplayMeshExtractor.GetGeometryDisplayMesh(geometry), null);
    }

    // preserve original behavior if the object is not far from origin
    if (!TryGetTranslationVector(geometry, out RG.Vector3d vectorToGeometry))
    {
      return (DisplayMeshExtractor.GetGeometryDisplayMesh(geometry), null);
    }

    // if the object is far from origin and risking faulty meshes due to precision errors: then duplicate geometry and move to origin first
    RG.GeometryBase geometryToMesh = geometry.Duplicate();
    geometryToMesh.Transform(RG.Transform.Translation(-vectorToGeometry));
    RG.Mesh displayMesh = DisplayMeshExtractor.GetGeometryDisplayMesh(geometryToMesh);

    return (displayMesh, vectorToGeometry);
  }

  /// <summary>
  /// Returns the duplicate of geometry and its Bbox center, if the precision errors are expected, and we will need to move the geometry to origin first.
  /// </summary>
  private static bool TryGetTranslationVector(RG.GeometryBase geom, out RG.Vector3d vector)
  {
    vector = new RG.Vector3d();
    var geometryBbox = geom.GetBoundingBox(false); // 'false' for 'accurate' parameter to accelerate bbox calculation
    if (geometryBbox.Min.DistanceTo(RG.Point3d.Origin) > 1e6 || geometryBbox.Max.DistanceTo(RG.Point3d.Origin) > 1e6)
    {
      vector = new RG.Vector3d(geometryBbox.Center);
      return true;
    }

    return false;
  }
}
